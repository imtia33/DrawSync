# DrawSync Architecture & Implementation Plan

## 1. System Overview

DrawSync is a tiered drawing collaboration platform where:
- **Free tier**: Local browser database only, 1 board, 3 teammates, no cloud backup.
- **Pro tier**: Local browser database + optional cloud backup, 5 boards, 15 teammates, $10.99/month.
- **Architecture**: ASP.NET Core server with embedded websocket layer, client-side browser local DB, Appwrite cloud control plane.

```
┌─────────────────────────────────────────────────────────────────┐
│                        Browser (Client)                          │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ DrawSync Canvas App (React/Vue/Vanilla JS)               │   │
│  │ - Real-time drawing UI                                   │   │
│  │ - Local DB persistence (IndexedDB/SQLite.wasm)           │   │
│  │ - Websocket event sending                                │   │
│  └──────────────────────────────────────────────────────────┘   │
│                            ↕ (Websocket)                         │
│                    ↕ (HTTP POST/GET - Backup)                    │
└─────────────────────────────────────────────────────────────────┘
                              ↕
┌─────────────────────────────────────────────────────────────────┐
│           ASP.NET Core Server (Embedded Websocket)               │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Websocket Server (Custom Implementation)                 │   │
│  │ - Connection management (1000 concurrent)                │   │
│  │ - Event relay & synchronization                          │   │
│  │ - Presence tracking (cursors, joins/leaves)              │   │
│  │ - Real-time broadcast to session members                 │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ REST API Layer                                            │   │
│  │ - Team/Board management                                  │   │
│  │ - Cloud backup endpoints (Pro users)                     │   │
│  │ - Plan entitlement checks                                │   │
│  │ - Authentication/Authorization                           │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Service Layer                                             │   │
│  │ - Team service (create, list, add members)               │   │
│  │ - Board service (CRUD, entitlement enforcement)          │   │
│  │ - Backup service (snapshot, restore, versioning)         │   │
│  │ - Plan service (seat limits, board limits)               │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              ↕
┌─────────────────────────────────────────────────────────────────┐
│              Appwrite Cloud (TablesDB)                            │
│  - Users, Teams, TeamMembers, Boards (metadata), BoardBackups   │
│  - Cloud backups stored on-demand (Pro users only)              │
│  - Appwrite Auth for identity                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Websocket Server Design

### 2.1 Technology Stack
- **Framework**: ASP.NET Core with WebSocket middleware (System.Net.WebSockets)
- **Hosting**: Embedded in the same ASP.NET Core process (single instance MVP)
- **Event Protocol**: JSON-based, versioned (v1 initially)
- **Connection Model**: Long-lived, stateful per board session

### 2.2 Connection Flow

```
Client Connect
    ↓
POST /api/teams/{teamId}/boards/{boardId}/join (HTTP)
  → Validate user auth, team membership, board access
  → Return session-specific token + websocket URL
    ↓
WebSocket /ws?token=SESSION_TOKEN&boardId=BOARD_ID
  → Server creates connection object
  → Server tracks client in session
  → Send "connected" ACK + current presence list
  → Client ready to send/receive events
```

### 2.3 Event Types

| Event | Direction | Payload | Notes |
|---|---|---|---|
| `stroke` | Client → Server → All | `{ points[], color, width, style }` | Drawing event; server relays to all session members |
| `strokeUpdate` | Client → Server → All | `{ strokeId, updates }` | Update existing stroke (color, width, etc.) |
| `cursorMove` | Client → Server → All | `{ x, y, userId, userName }` | Presence indicator; sent at ~15 fps |
| `presence` | Server → All | `{ userId, action: 'join'\|'leave', users[] }` | User joined/left; also send full list on new join |
| `ack` | Server → Sender | `{ eventId, status: 'ok'\|'error' }` | Confirms receipt; client removes from pending queue |
| `snapshot` | Server → Client | `{ strokes[] }` | Sent on join for recovery; full board state |

### 2.4 Message Format

```json
{
  "id": "unique-event-id-uuid",
  "type": "stroke",
  "timestamp": "2026-05-12T10:30:00Z",
  "sender": "user-id",
  "payload": {
    "points": [[10, 20], [15, 25], [20, 30]],
    "color": "#ff0000",
    "width": 2,
    "style": "solid"
  }
}
```

### 2.5 Server-Side Session Management

```csharp
// Pseudocode
class WebsocketSession {
    string BoardId { get; set; }
    Dictionary<string, WebsocketClient> Clients { get; set; } // userId -> connection
    Queue<DrawingEvent> EventBuffer { get; set; }
    DateTime CreatedAt { get; set; }
    
    void Broadcast(DrawingEvent evt) {
        // Send to all connected clients in this session
        foreach (var client in Clients.Values) {
            client.SendAsync(evt);
        }
    }
    
    void HandleStrokeEvent(DrawingEvent evt) {
        // 1. Validate (layer exists, user permission)
        // 2. Buffer event (for recovery)
        // 3. Broadcast to all clients
        // 4. Send ACK back to sender
    }
}
```

### 2.6 Concurrency & Scaling (MVP)

- **Single instance**: All 1000 concurrent connections in one ASP.NET Core process.
- **Session isolation**: Each board session is independent; no cross-board communication.
- **Event buffering**: Keep last 1000 events per session in memory for client recovery (reconnection).
- **Presence timeout**: If no heartbeat for 30s, remove client from session.
- **Load testing target**: 1000 concurrent users, ~100ms latency p95 on LAN.

---

## 3. Browser Local Database Strategy

### 3.1 Technology Evaluation

**Candidate Options:**

| Tech | Pros | Cons | Recommendation |
|---|---|---|---|
| **IndexedDB** | Async, large storage (~50MB+), broad browser support | More complex API, harder to query | ✅ **Recommended** |
| **SQLite (sql.js)** | Relational, fast queries, good for millions of rows | Additional WASM load (~500KB), slower startup | ⚠️ Consider for future |
| **LocalStorage** | Simple API, synchronous | 5-10MB limit, blocks main thread | ❌ Not suitable |

**Recommendation**: Start with **IndexedDB** for MVP. Reasons:
- Sufficient storage for typical drawings
- Good balance of complexity and performance
- Wide browser support (98%+)
- Migration path to SQLite.wasm if queries become a bottleneck

### 3.2 IndexedDB Schema

```javascript
// Database: "DrawSync"
// Stores within it:

// Store: "boards"
// Keypath: "boardId"
{
  boardId: "board-123",
  teamId: "team-1",
  name: "My Drawing",
  type: "whiteboard", // or "map"
  createdAt: "2026-05-12T10:00:00Z",
  updatedAt: "2026-05-12T10:30:00Z",
  archived: false
}

// Store: "strokes"
// Keypath: "id", indexed by "boardId"
{
  id: "stroke-abc",
  boardId: "board-123",
  points: [[10, 20], [15, 25], [20, 30]],
  color: "#ff0000",
  width: 2,
  style: "solid",
  createdBy: "user-1",
  createdAt: "2026-05-12T10:15:00Z",
  updatedAt: "2026-05-12T10:15:00Z"
}

// Store: "symbols" (for map boards)
// Keypath: "id", indexed by "boardId"
{
  id: "symbol-xyz",
  boardId: "board-123",
  type: "bridge", // or "building", "crane", etc.
  x: 100,
  y: 200,
  createdBy: "user-1",
  createdAt: "2026-05-12T10:15:00Z"
}

// Store: "backups"
// Keypath: "id", indexed by "boardId"
{
  id: "backup-1",
  boardId: "board-123",
  version: 1,
  payload: "{ ... full board snapshot ... }",
  checksum: "sha256hash",
  createdBy: "user-1",
  createdAt: "2026-05-12T10:15:00Z",
  syncedToCloud: true
}
```

### 3.3 Loading & Performance

- **Initial load**: Fetch all boards for the team on login.
  - Use compound index `["teamId", "updatedAt"]` to check for stale data.
  - For millions of rows per board, implement pagination: load first 100 strokes, then lazy-load on canvas scroll/pan.
- **Lazy loading**: Load strokes on demand as user pans canvas.
  - Use spatial index (quadtree) if available, or filter by viewport bounds.
- **UI blocking**: All IndexedDB operations are async; avoid blocking main thread.

---

## 4. Cloud Backup Flow (Pro Users)

### 4.1 Backup Trigger

When a Pro user presses **Save** or after N minutes of idle (configurable):

```
Client (Local DB)
    ↓
Collect all strokes/layers for board
    ↓
POST /api/teams/{teamId}/boards/{boardId}/backup
  Payload: {
    teamId: "team-1",
    boardId: "board-123",
    version: 5,
    payload: "{ layers: [...], strokes: [...] }",
    checksum: "sha256hash"
  }
    ↓
ASP.NET Core Server
  1. Verify Pro plan on team
  2. Validate checksum
  3. Create row in board_backups table (Appwrite)
  4. Return backup ID + timestamp
    ↓
Client
  1. Update local backup metadata (synced = true, cloudBackupId)
  2. Show "Saved to cloud" notification
  3. Resume editing
```

### 4.2 Conflict Resolution for Backup

Since backup is **immediate sync (blocking)**, there are no conflicts:
- The server version is authoritative for cloud backups.
- Local edits continue on the browser; backup is a snapshot in time.
- Next Save will overwrite the previous backup version.

### 4.3 Restore Flow

When user loads a board with a cloud backup:

```
Client joins board
    ↓
GET /api/teams/{teamId}/boards/{boardId}/backups
    ↓
Server returns backup history with versions
    ↓
User selects version to restore (or latest)
    ↓
POST /api/teams/{teamId}/boards/{boardId}/restore?version=5
    ↓
Server returns snapshot payload
    ↓
Client clears local board state
    ↓
Client inserts all layers, strokes, symbols from snapshot into IndexedDB
    ↓
Canvas re-renders from local DB
```

---

## 6. Data Synchronization & Consistency

### 5.1 Event-Based Delta Sync

Each drawing action is sent as a discrete event (stroke, strokeUpdate, layerCreate, etc.):

1. **Client sends** stroke event (local optimistic update to IndexedDB, UI shows immediately)
2. **Server receives**, validates, buffers, broadcasts to all clients
3. **All clients receive** event, apply to local DB
4. **Server sends ACK** to sender

This ensures all clients converge to the same state without needing full snapshots.

### 5.2 Conflict Resolution Strategy

**Server Authority** (recommended):

- If two clients send conflicting events (e.g., both try to delete the same stroke), the server processes them in order received.
- Each stroke has a unique ID; overlapping strokes don't conflict (they coexist on the same canvas, just like two people writing on paper).
- If both clients edit the *same* stroke simultaneously (extremely rare), the server applies the later event (acceptable for MVP).

For future: Consider **OT (Operational Transformation)** to handle true conflict-free edits.

### 5.3 Recovery & Reconnection

If a client loses websocket connection:

1. Client detects connection drop (no heartbeat for 5s).
2. Client queues pending events locally.
3. Client attempts to reconnect with exponential backoff.
4. On reconnect, server sends full board snapshot (layers, strokes, symbols).
5. Client merges snapshot with pending local edits:
   - Strokes that were sent before disconnect: already in snapshot, discard.
   - Strokes queued but not sent: send now after snapshot applied.
6. Canvas re-renders.

---

## 4. Stroke Ownership & Permissions

### 4.1 Data Model

Each stroke includes a `createdBy` field (user ID):

```javascript
{
  id: "stroke-abc",
  boardId: "board-123",
  points: [[10, 20], [15, 25], [20, 30]],
  color: "#ff0000",
  width: 2,
  style: "solid",
  createdBy: "user-1",  // Creator's user ID
  createdAt: "2026-05-12T10:15:00Z",
  updatedAt: "2026-05-12T10:15:00Z"
}
```

### 4.2 Permission Rules

| Action | Allowed | Condition |
|---|---|---|
| **Draw new stroke** | All members | Must have edit permission on board |
| **Edit stroke** | Owner | User ID matches `createdBy` |
| **Delete stroke** | Owner | User ID matches `createdBy` |
| **View stroke** | All members | Visible on board |

### 4.3 Client-Side Permission Checks

When rendering the canvas:

```javascript
// Pseudocode
for (const stroke of board.strokes) {
  if (stroke.createdBy === currentUser.id) {
    // Render as editable (highlight on hover, show edit/delete buttons)
    canvas.renderStroke(stroke, { editable: true });
  } else {
    // Render as read-only (different color/opacity, no interaction)
    canvas.renderStroke(stroke, { editable: false, opacity: 0.9 });
  }
}
```

### 4.4 Server-Side Validation

On every edit/delete request, the server validates ownership:

```csharp
// Pseudocode
if (strokeUpdateEvent.StrokeId != null) {
  var stroke = await GetStroke(strokeUpdateEvent.StrokeId);
  if (stroke.CreatedBy != currentUser.Id) {
    return Error("Permission denied: you can only edit your own strokes");
  }
  // Proceed with update
}
```

This prevents malicious clients from bypassing UI restrictions.

### 4.5 UI Feedback

- **Own strokes**: Normal rendering, hover shows edit/delete buttons
- **Others' strokes**: Slightly grayed out or dimmed, label showing creator name (e.g., "By Alice"), no interaction
- **On delete attempt of others' stroke**: Show tooltip: "You can only delete your own strokes"

---

## 5. Plan Enforcement & Entitlements

### 6.1 At-Join Checks

When a user joins a board session:

```csharp
// Pseudocode
var team = await appwriteDb.GetTeam(teamId);
var membership = await appwriteDb.GetTeamMember(teamId, userId);

// Check role: only Admin/Member can join; Viewer can join but read-only
if (membership.Role == "Viewer") {
    // Enable read-only mode: block all drawing events
    client.SendReadOnlyMode();
}

// Check board count (already enforced at board creation, not here)

// Check seat limit (already enforced at invite, not here)
```

### 6.2 At-Board-Creation Checks

When creating a new board:

```csharp
var team = await appwriteDb.GetTeam(teamId);
var boardCount = await appwriteDb.CountBoards(teamId);

int boardLimit = team.PlanTier == "free" ? 1 : 5;
if (boardCount >= boardLimit) {
    return 403 Forbidden: "Board limit reached for your plan";
}

// Create board in Appwrite
await appwriteDb.CreateBoard(board);
```

### 6.3 At-Backup Checks

When triggering a backup (Pro only):

```csharp
var team = await appwriteDb.GetTeam(teamId);
if (team.PlanTier != "pro") {
    return 403 Forbidden: "Backup is only available on Pro plan";
}

// Proceed with backup
await CreateBackupSnapshot(teamId, boardId, payload);
```

---

## 7. Testing Strategy

### 7.1 Websocket Connection/Reconnection Tests

**Test Suite**: `WebsocketConnectionTests`

- [ ] Client connects successfully with valid token
- [ ] Client receives full board snapshot on join
- [ ] Client receives presence list on join
- [ ] Client auto-reconnects on connection drop
- [ ] Client queues events during disconnection
- [ ] Reconnected client merges queued events with server state
- [ ] Connection timeout after 30s of no heartbeat
- [ ] Invalid token rejected
- [ ] Multiple clients in same session receive each other's events

### 7.2 Data Consistency Tests

**Test Suite**: `DataConsistencyTests`

- [ ] All clients in session converge to same board state after 10 concurrent strokes
- [ ] Layer creation broadcast to all clients
- [ ] Layer deletion cascades to strokes in local DB on all clients
- [ ] Stroke updates (color, width) reflected on all clients within 100ms
- [ ] Concurrent edits to same stroke: later write wins
- [ ] Presence updates (cursor position) received at ≥15 fps
- [ ] Clients rejoin session and recover missing events from buffer

### 7.3 Local-to-Cloud Backup Tests

**Test Suite**: `BackupFlowTests`

- [ ] Free tier user cannot trigger backup
- [ ] Pro tier user backup succeeds
- [ ] Backup creates row in Appwrite board_backups table
- [ ] Backup includes full layer, stroke, symbol state
- [ ] Restore from backup clears local board and loads snapshot
- [ ] Multiple backup versions tracked correctly
- [ ] Restore older version does not affect latest backup
- [ ] Checksum validated on restore

### 7.4 Load Testing

**Test Suite**: `LoadTests` (using k6 or similar)

- [ ] 1000 concurrent websocket connections to server
- [ ] 100 concurrent connections per session (10 sessions)
- [ ] Each connection sends 1 stroke every 1-2 seconds
- [ ] Measure latency: p50, p95, p99 on stroke broadcast
- [ ] Verify no packet loss
- [ ] Memory usage under sustained load
- [ ] CPU usage under sustained load
- [ ] Graceful degradation above 1000 connections

---

## 8. Deployment & Operations

### 8.1 Single Instance (MVP)

- **Host**: Single ASP.NET Core instance running on Linux/Windows.
- **Port**: 5000 (HTTP) + 5001 (HTTPS) for REST API and websocket.
- **Websocket URL**: `wss://drawsync.example.com/ws`
- **Database**: Appwrite cloud for control plane; local IndexedDB for drawing data.
- **Monitoring**: Application Insights or similar for latency, error rates, connection counts.
- **Scaling path**: If needed, implement sticky load balancer + Redis session store.

### 8.2 Operational Checklist

- [ ] Monitor websocket connection count (alert if > 1100).
- [ ] Monitor event buffer size per session (alert if > 10MB).
- [ ] Monitor Appwrite API call rate (backup/restore operations).
- [ ] Daily backup of Appwrite database (managed by Appwrite).
- [ ] Implement connection limit enforcement (soft limit 1000, hard limit 1200).
- [ ] Log all websocket events (for debugging/audit).
- [ ] Implement graceful shutdown: drain existing connections before restart.

---

## 9. Future Enhancements

- **Session recording**: Store event sequence for playback/replay.
- **OT/CRDT**: Replace server authority with true concurrent editing.
- **Horizontal scaling**: Multiple server instances with Redis pub/sub for cross-instance broadcasting.
- **SQLite.wasm migration**: For boards with millions of strokes, migrate local DB.
- **Offline support**: Sync when connectivity returns (currently out of scope).
- **Voice/Video chat**: Integrate a real-time communication API.
- **Advanced presence**: Show who is editing which layer, not just cursor.

---

## 10. Risk Mitigation

| Risk | Mitigation |
|---|---|
| Websocket memory leak | Implement connection cleanup on disconnect; monitor resident memory. |
| Event buffer overflow | Limit buffer to 1000 events/session; drop oldest on overflow. |
| Appwrite quota exceeded | Monitor API calls; alert team if approaching rate limit. |
| Client local DB corruption | Implement checksum validation; allow manual "sync from cloud" button. |
| Thundering herd on reconnect | Stagger snapshot sends; implement backpressure. |
| Single point of failure | (MVP only; add load balancing later) |

