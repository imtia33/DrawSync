# DrawSync Technical Deep Dive & API Specification

## 1. Concurrent Drawing & Data Consistency

In DrawSync, multiple users draw on the same canvas simultaneously (like two people writing on paper). There are **no conflicts** in the traditional sense:

- **Strokes are independent**: When Client A draws stroke #1 and Client B draws stroke #2, both exist on the canvas. They may overlap visually, and that's fine—it's expected collaborative behavior.
- **Server processes events sequentially**: Each websocket event (stroke, strokeUpdate, etc.) is received and relayed to all clients in order. This ensures all clients see the same drawing history.
- **Last-write-wins for edits**: If both clients try to edit the same stroke (e.g., change its color) simultaneously, the server applies edits sequentially. The later edit wins. This is acceptable because stroke edits are rare compared to new strokes being added.

**Design principle**: Overlapping strokes are not a problem; they're the intended design. Just like two people writing on the same piece of paper.

---

## 2. REST API Specification

### 2.1 Authentication

All endpoints require `Authorization: Bearer {token}` header (Appwrite JWT).

```bash
curl -H "Authorization: Bearer eyJhbGciOi..." https://drawsync.example.com/api/teams
```

---

### 2.2 Team Endpoints

#### Create Team
```
POST /api/teams

Request Body:
{
  "name": "My Team"
}

Response (201 Created):
{
  "id": "team-123",
  "name": "My Team",
  "planTier": "free",
  "boardLimit": 1,
  "seatLimit": 3,
  "extraSeatPacks": 0,
  "backupEnabled": false,
  "createdAt": "2026-05-12T10:00:00Z"
}
```

#### List Teams (for current user)
```
GET /api/teams?limit=20&offset=0

Response (200 OK):
{
  "teams": [
    {
      "id": "team-123",
      "name": "My Team",
      "planTier": "free",
      "role": "admin",  // current user's role in this team
      "memberCount": 2
    }
  ],
  "total": 1
}
```

#### Get Team Details
```
GET /api/teams/{teamId}

Response (200 OK):
{
  "id": "team-123",
  "name": "My Team",
  "planTier": "free",
  "boardLimit": 1,
  "seatLimit": 3,
  "extraSeatPacks": 0,
  "backupEnabled": false,
  "members": [
    { "userId": "user-1", "role": "admin", "name": "Alice" },
    { "userId": "user-2", "role": "member", "name": "Bob" }
  ]
}

Errors:
- 404: Team not found
- 403: Not a member of this team
```

#### Invite Team Member
```
POST /api/teams/{teamId}/members

Request Body:
{
  "email": "bob@example.com",
  "role": "member"  // "member" or "viewer"
}

Response (201 Created):
{
  "id": "invite-456",
  "email": "bob@example.com",
  "role": "member",
  "token": "invite_token_123",
  "expiresAt": "2026-05-19T10:00:00Z"
}

Errors:
- 400: Email already in team
- 403: Not an admin of this team
- 429: Seat limit reached
```

#### Remove Team Member
```
DELETE /api/teams/{teamId}/members/{userId}

Response (204 No Content)

Errors:
- 404: Member not found
- 403: Not an admin
- 400: Cannot remove yourself
```

#### Change Member Role
```
PATCH /api/teams/{teamId}/members/{userId}

Request Body:
{
  "role": "viewer"
}

Response (200 OK):
{
  "userId": "user-2",
  "role": "viewer"
}

Errors:
- 403: Not an admin
```

---

### 2.3 Board Endpoints

#### Create Board
```
POST /api/teams/{teamId}/boards

Request Body:
{
  "name": "My Drawing",
  "type": "whiteboard"  // or "map"
}

Response (201 Created):
{
  "id": "board-789",
  "teamId": "team-123",
  "name": "My Drawing",
  "type": "whiteboard",
  "archived": false,
  "createdBy": "user-1",
  "createdAt": "2026-05-12T10:00:00Z"
}

Errors:
- 403: Not a member of team
- 429: Board limit reached for plan tier
```

#### List Boards (for team)
```
GET /api/teams/{teamId}/boards?limit=20&offset=0

Response (200 OK):
{
  "boards": [
    {
      "id": "board-789",
      "name": "My Drawing",
      "type": "whiteboard",
      "archived": false,
      "createdBy": "user-1",
      "createdAt": "2026-05-12T10:00:00Z",
      "memberCount": 2
    }
  ],
  "total": 1
}
```

#### Get Board Details
```
GET /api/teams/{teamId}/boards/{boardId}

Response (200 OK):
{
  "id": "board-789",
  "teamId": "team-123",
  "name": "My Drawing",
  "type": "whiteboard",
  "archived": false,
  "createdBy": "user-1",
  "createdAt": "2026-05-12T10:00:00Z",
  "layers": [
    {
      "id": "layer-1",
      "name": "Layer 1",
      "visible": true
    }
  ]
}
```

#### Update Board
```
PATCH /api/teams/{teamId}/boards/{boardId}

Request Body:
{
  "name": "New Name",
  "archived": true
}

Response (200 OK):
{ ... }

Errors:
- 400: Cannot un-archive a board (once archived, read-only)
```

#### Delete Board
```
DELETE /api/teams/{teamId}/boards/{boardId}

Response (204 No Content)

Errors:
- 403: Not an admin
- Note: Deletion cascades to backups on server; client deletes local strokes
```

---

### 2.4 Backup Endpoints

#### Create Backup (Pro only)
```
POST /api/teams/{teamId}/boards/{boardId}/backup

Request Body:
{
  "payload": "{ \"strokes\": [...], \"symbols\": [...] }",  // JSON string
  "checksum": "sha256hash..."
}

Response (201 Created):
{
  "id": "backup-1",
  "boardId": "board-789",
  "version": 1,
  "createdBy": "user-1",
  "createdAt": "2026-05-12T10:15:00Z"
}

Errors:
- 403: Free tier team (backup not available)
- 400: Checksum mismatch (payload corrupted)
```

#### List Backups
```
GET /api/teams/{teamId}/boards/{boardId}/backups?limit=10

Response (200 OK):
{
  "backups": [
    {
      "id": "backup-1",
      "version": 1,
      "createdBy": "user-1",
      "createdAt": "2026-05-12T10:15:00Z"
    }
  ],
  "total": 1
}

Errors:
- 403: Free tier team
```

#### Get Backup (with payload)
```
GET /api/teams/{teamId}/boards/{boardId}/backups/{backupId}

Response (200 OK):
{
  "id": "backup-1",
  "version": 1,
  "payload": "{ \"strokes\": [...], \"symbols\": [...] }",
  "checksum": "sha256hash...",
  "createdBy": "user-1",
  "createdAt": "2026-05-12T10:15:00Z"
}

Errors:
- 403: Free tier team
```

#### Restore from Backup
```
POST /api/teams/{teamId}/boards/{boardId}/restore?version=1

Response (200 OK):
{
  "payload": "{ \"strokes\": [...], \"symbols\": [...] }",
  "checksum": "sha256hash..."
}

Client Flow:
1. Receive payload from server
2. Validate checksum
3. Clear local board (strokes and symbols)
4. Insert all items from payload into IndexedDB
5. Re-render canvas
6. Broadcast to all connected clients (via websocket): "Board restored to version 1"
```

---

### 2.5 Websocket Join Endpoint

#### Get Websocket Session Token
```
POST /api/teams/{teamId}/boards/{boardId}/join

Request Body: {} (empty)

Response (200 OK):
{
  "sessionToken": "jwt_token_for_websocket",
  "websocketUrl": "wss://drawsync.example.com/ws",
  "boardId": "board-789",
  "sessionId": "session-uuid"
}

Client Flow:
1. Receive token and URL from API
2. Connect to websocket: new WebSocket(url + "?token=" + token)
3. Send websocket event: { type: "join", boardId, userId }
4. Receive snapshot + presence list from server
```

---

## 3. Websocket Event Specification

### 3.1 Client → Server Events

#### `stroke` - New Stroke
```json
{
  "id": "evt-uuid-1",
  "type": "stroke",
  "timestamp": "2026-05-12T10:15:30.123Z",
  "payload": {
    "points": [[10, 20], [15, 25], [20, 30]],
    "color": "#ff0000",
    "width": 2,
    "style": "solid"
  }
}

Server Response:
{
  "id": "evt-uuid-1",
  "type": "ack",
  "status": "ok",
  "data": {
    "strokeId": "stroke-abc"  // Server-assigned ID
  }
}
```

#### `strokeUpdate` - Update Existing Stroke
```json
{
  "id": "evt-uuid-2",
  "type": "strokeUpdate",
  "timestamp": "2026-05-12T10:15:31.000Z",
  "payload": {
    "strokeId": "stroke-abc",
    "updates": {
      "color": "#0000ff",
      "width": 3
    }
  }
}

Server Response:
{
  "id": "evt-uuid-2",
  "type": "ack",
  "status": "ok"
}
```

#### `cursorMove` - Presence Indicator
```json
{
  "id": "evt-uuid-3",
  "type": "cursorMove",
  "timestamp": "2026-05-12T10:15:30.200Z",
  "payload": {
    "x": 150,
    "y": 250
  }
}

Note: No ACK for cursor events (fire-and-forget)
```



---

### 3.2 Server → All Clients Events

#### `stroke` - Relay Incoming Stroke
```json
{
  "id": "evt-uuid-1",
  "type": "stroke",
  "timestamp": "2026-05-12T10:15:30.123Z",
  "sender": "user-1",
  "payload": {
    "strokeId": "stroke-abc",  // Server-assigned
    "points": [[10, 20], [15, 25], [20, 30]],
    "color": "#ff0000",
    "width": 2,
    "style": "solid"
  }
}

Client Flow (Non-Sender):
1. Receive event
2. Insert stroke into local IndexedDB strokes table
3. Render stroke on canvas
```

#### `presence` - Join/Leave Broadcast
```json
{
  "type": "presence",
  "action": "join",  // or "leave"
  "user": {
    "userId": "user-2",
    "name": "Bob",
    "color": "#00ff00"  // Avatar color
  },
  "users": [
    { "userId": "user-1", "name": "Alice", "color": "#ff0000" },
    { "userId": "user-2", "name": "Bob", "color": "#00ff00" }
  ]
}

Client Flow:
1. Update member list in UI
2. Add/remove cursor from canvas
```

#### `snapshot` - Full Board State (on join)
```json
{
  "type": "snapshot",
  "payload": {
    "layers": [
      { "id": "layer-1", "name": "Layer 1", "visible": true },
      { "id": "layer-2", "name": "Layer 2", "visible": false }
    ],
    "strokes": [
      {
        "id": "stroke-abc",
        "layerId": "layer-1",
        "points": [...],
        "color": "#ff0000",
        "width": 2,
        "style": "solid"
      }
    ],
    "symbols": [
      {
        "id": "symbol-xyz",
        "type": "bridge",
        "x": 100,
        "y": 200
      }
    ]
  }
}

Client Flow:
1. Receive snapshot on join
2. Clear local board (or validate consistency)
3. Insert all strokes and symbols into IndexedDB
4. Re-render canvas from local DB
```

#### `ack` - Acknowledgment
```json
{
  "id": "evt-uuid-1",
  "type": "ack",
  "status": "ok",  // or "error"
  "error": null   // null if ok, error message if not
}

Client Flow:
1. Match ACK id to sent event
2. Remove from pending queue
3. If error: show user notification
```

---

## 4. Error Handling

### 4.1 HTTP Error Codes

| Code | Meaning |
|---|---|
| 400 | Bad Request (validation error) |
| 401 | Unauthorized (missing/invalid token) |
| 403 | Forbidden (not allowed for this user/plan) |
| 404 | Not Found |
| 429 | Too Many Requests (quota/limit exceeded) |
| 500 | Internal Server Error |

### 4.2 Websocket Error Messages

```json
{
  "type": "error",
  "message": "Board not found",
  "code": "BOARD_NOT_FOUND"
}
```

Common Errors:
- `UNAUTHORIZED` - Invalid or expired token
- `BOARD_NOT_FOUND` - Board deleted or access denied
- `PERMISSION_DENIED` - User is Viewer, cannot edit
- `STROKE_NOT_FOUND` - Stroke deleted concurrently
- `SERVER_ERROR` - Internal error

---

## 5. Client-Side Optimistic Updates

To provide responsive UI, the client applies changes locally before server ACK:

```javascript
// User draws stroke
const stroke = {
  id: generateUUID(),  // Temporary ID
  points: [...],
  color: "#ff0000",
  width: 2,
  style: "solid"
};
  style: "solid"
};

// Step 1: Insert into local IndexedDB (optimistic)
await db.strokes.insert(stroke);

// Step 2: Render on canvas immediately
canvas.drawStroke(stroke);

// Step 3: Send to server (async)
websocket.send({
  type: "stroke",
  payload: stroke
});

// Step 4: Receive ACK with server-assigned ID
websocket.on("ack", (ack) => {
  if (ack.status === "ok") {
    // Replace temporary ID with server ID
    await db.strokes.update(stroke.id, { id: ack.data.strokeId });
  } else {
    // Rollback: delete from local DB
    await db.strokes.delete(stroke.id);
    canvas.undoStroke(stroke.id);
    showError("Failed to save stroke");
  }
});
```

This ensures the UI is responsive even on high-latency connections.

---

## 6. Implementation Checklist

- [ ] Conflict resolution strategy documented and agreed
- [ ] REST API endpoints implemented and tested
- [ ] Websocket event types defined and implemented
- [ ] Error handling consistent across HTTP and Websocket
- [ ] Optimistic update logic working on client
- [ ] Server-side validation of all events
- [ ] Rate limiting on backups (e.g., 1 per minute)
- [ ] Checksum validation for backup integrity
- [ ] Client-side retry logic with exponential backoff
- [ ] Monitoring/logging of all API calls and websocket events

