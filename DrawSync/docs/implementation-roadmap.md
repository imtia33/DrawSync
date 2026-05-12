# DrawSync Implementation Roadmap

## Overview

This document outlines a **6-week implementation plan** for the DrawSync MVP. The work is split into **12 sequential tasks**: two tasks per week, completed one after the other by two teammates. Appwrite is used for authentication, built-in team management, and control-plane metadata. Drawing data stays local in the browser during MVP, and Appwrite cloud backup is included in the same plan.

---

## Week 1: Appwrite Foundation

### Goal
Set up Appwrite Auth, built-in Teams, and the minimum board metadata needed to support collaboration.

### Deliverables

#### 1.1 Appwrite Auth + Built-in Teams
- **Task**: Use Appwrite Auth and the built-in Teams/Memberships system for team management
- **Scope**:
  - Sign up, login, session handling
  - Create team via Appwrite Teams API
  - Invite members and assign roles via Appwrite memberships
  - Enforce access with Appwrite roles and permissions
- **Acceptance Criteria**:
  - Users can create and join teams without a custom teams table
  - Team creator has owner access
  - Membership roles work with `Role.team(teamId)` and `Role.team(teamId, role)`
  - Multi-tenant isolation verified with Appwrite permissions
- **Estimate**: 3 days
- **Owner**: Backend

#### 1.2 Board Metadata Table + API
- **Task**: Create Appwrite table and REST API for board metadata only
- **Table**:
  - `boards` - id, teamId, name, type, archived, createdBy, createdAt
- **Endpoints**:
  - `POST /api/teams/{teamId}/boards` - Create board
  - `GET /api/teams/{teamId}/boards` - List boards
  - `GET /api/teams/{teamId}/boards/{boardId}` - Get board metadata
  - `PATCH /api/teams/{teamId}/boards/{boardId}` - Update/archive
  - `DELETE /api/teams/{teamId}/boards/{boardId}` - Delete board
- **Acceptance Criteria**:
  - Board creation respects plan limits
  - Archived boards cannot be edited
  - Board metadata is scoped to the team
- **Estimate**: 4 days
- **Owner**: Backend

#### 1.3 Plan Enforcement Service
- **Task**: Enforce Free/Pro limits against Appwrite teams and boards
- **Rules**:
  - Free: 1 board, 3 teammates
  - Pro: 5 boards, 15 teammates
- **Methods**:
  - `CanCreateBoard(teamId)`
  - `CanAddMember(teamId)`
  - `GetPlanLimits(teamId)`
- **Acceptance Criteria**:
  - Team board/member counts are checked before write operations
  - Limits block creation cleanly with user-facing errors
- **Estimate**: 2 days
- **Owner**: Backend

### Week 1 Acceptance
- [ ] Task 1.1 completed and pushed before Task 1.2 begins
- [ ] Users can create and join Appwrite teams
- [ ] Board metadata is stored separately from team management
- [ ] Plan limits are enforced for boards and seats

---

## Week 2: Real-Time Core

### Goal
Implement websocket event flow and creator-based stroke permissions.

### Deliverables

#### 2.1 Event Protocol & Serialization
- **Task**: Implement websocket event types and relay logic
- **Events**:
  - `stroke` - New stroke with `createdBy`
  - `strokeUpdate` - Update own stroke only
  - `strokeDelete` - Delete own stroke only
  - `cursorMove` - Presence updates
  - `presence` - Join/leave notifications
  - `snapshot` - Full board state on join
  - `ack` - Acknowledgment
- **Acceptance Criteria**:
  - Events serialized as JSON
  - Broadcast within 50ms to session clients
  - Relay payloads preserve `createdBy`
  - Server validates mutation ownership before relay
- **Estimate**: 3 days
- **Owner**: Backend

#### 2.2 Stroke Permission Validation
- **Task**: Enforce owner-only edit/delete on the server
- **Logic**:
  - `strokeUpdate`: sender must match `stroke.createdBy`
  - `strokeDelete`: sender must match `stroke.createdBy`
  - `stroke`: server sets `createdBy` from authenticated sender
  - Return `PERMISSION_DENIED` on mismatch
- **Acceptance Criteria**:
  - Owners can edit/delete their strokes
  - Non-owners receive a clear permission error
  - Unauthorized mutations are not relayed
- **Estimate**: 2 days
- **Owner**: Backend

### Week 2 Acceptance
- [ ] Task 2.1 completed and pushed before Task 2.2 begins
- [ ] Client A sends stroke; Client B receives within 50ms
- [ ] Client A cannot edit/delete Client B's stroke
- [ ] Server rejects unauthorized mutations with PERMISSION_DENIED

---

## Week 3: Local Persistence

### Goal
Implement browser local storage and reconnect behavior.

### Deliverables

#### 3.1 IndexedDB Schema
- **Task**: Set up browser local database schema
- **IndexedDB Stores**:
  - `boards` - id, teamId, name, createdAt
  - `strokes` - id, boardId, points, color, width, createdBy, createdAt, updatedAt
  - `symbols` - id, boardId, type, x, y
- **Acceptance Criteria**:
  - IndexedDB initializes on first board open
  - Local stroke schema preserves createdBy
  - Store reads/writes are async and stable
- **Estimate**: 3 days
- **Owner**: Frontend

#### 3.2 Websocket Client & Recovery
- **Task**: Implement client websocket wrapper and reconnect handling
- **Client Library**:
  - `connect(boardId, token)`
  - `send(event)`
  - `on(eventType, handler)`
  - `disconnect()`
  - Auto-reconnect with exponential backoff
- **Acceptance Criteria**:
  - Events queue during disconnect and flush on reconnect
  - No duplicate events after reconnect
  - Snapshot restores local session state
- **Estimate**: 3 days
- **Owner**: Frontend

### Week 3 Acceptance
- [ ] Task 3.1 completed and pushed before Task 3.2 begins
- [ ] IndexedDB is ready before the canvas work starts
- [ ] Reconnect behavior restores queued events and snapshot

---

## Week 4: Canvas and Ownership UI

### Goal
Implement the canvas, ownership-aware UI, and local stroke sync.

### Deliverables

#### 4.1 Canvas Rendering Engine
- **Task**: Render the whiteboard canvas with pan/zoom
- **Features**:
  - Infinite 2D canvas
  - Pan and zoom
  - Render strokes from IndexedDB
  - Display creator name/label on strokes
- **Acceptance Criteria**:
  - Canvas renders strokes from local DB
  - Pan/zoom works smoothly
  - Brush strokes appear in real time
- **Estimate**: 4 days
- **Owner**: Frontend

#### 4.2 Stroke Ownership & Local Sync UI
- **Task**: Restrict editing/deleting to stroke owners and sync local edits
- **Features**:
  - Own strokes: highlight and show edit/delete controls
  - Other strokes: dimmed, read-only
  - Blocked actions show permission tooltip
  - Save stroke locally with `createdBy = currentUser.id`
- **Acceptance Criteria**:
  - Own strokes are selectable and editable
  - Non-owned strokes are not selectable for edit/delete
  - Local stroke persistence works without blocking UI
- **Estimate**: 4 days
- **Owner**: Frontend

### Week 4 Acceptance
- [ ] Task 4.1 completed and pushed before Task 4.2 begins
- [ ] Canvas renders local strokes and shows creator labels
- [ ] Ownership UI blocks edits/deletes on non-owned strokes

---

## Week 5: Backup and Remote Stroke Flow

### Goal
Add Appwrite backup and complete incoming stroke/presence handling.

### Deliverables

#### 5.1 Incoming Stroke Application & Presence
- **Task**: Apply remote strokes and show cursors
- **Features**:
  - Insert received stroke into IndexedDB
  - Render with ownership styling
  - Show live cursors from other users
- **Acceptance Criteria**:
  - Remote strokes appear within 50ms
  - `createdBy` is preserved locally
  - Cursors are visible and smooth
- **Estimate**: 3 days
- **Owner**: Frontend

#### 5.2 Appwrite Backup Table + API
- **Task**: Create Appwrite backup storage and REST endpoints
- **Table**:
  - `board_backups` - id, boardID, version, payload, checksum, createdBy, createdAt
- **Endpoints**:
  - `POST /api/teams/{teamId}/boards/{boardId}/backup` - Create backup snapshot
  - `GET /api/teams/{teamId}/boards/{boardId}/backups` - List backup versions
  - `POST /api/teams/{teamId}/boards/{boardId}/restore?version=X` - Restore backup
- **Acceptance Criteria**:
  - Only Pro teams can create backups
  - Backups include full board state
  - Checksum is validated on restore
  - Multiple backup versions are tracked
- **Estimate**: 3 days
- **Owner**: Backend

### Week 5 Acceptance
- [ ] Task 5.1 completed and pushed before Task 5.2 begins
- [ ] Remote strokes appear within 50ms
- [ ] Appwrite backup table and endpoints are implemented

---

## Week 6: Restore, Testing & Deployment

### Goal
Finish restore flow, validate the MVP end-to-end, and prepare for deployment.

### Deliverables

#### 6.1 Backup Snapshot Flow and Restore
- **Task**: Serialize the local board state, create backups, and restore snapshots
- **Flow**:
  - Client collects board strokes and symbols from IndexedDB
  - Serialize to JSON
  - Compute checksum
  - POST backup payload to the API
  - Restore by clearing local board and reloading snapshot data
- **Acceptance Criteria**:
  - Snapshot includes all drawing data
  - Checksum computed correctly
  - Backup and restore complete without blocking the UI
- **Estimate**: 3 days
- **Owner**: Frontend

#### 6.2 Integration Testing, Baseline Scalability, and Deployment Prep
- **Task**: Test complete user workflows and finalize the MVP
- **Scenarios**:
  - Create team, invite member, create board, draw, delete own stroke
  - Verify permission denial on editing/deleting another user’s stroke
  - Offline/reconnect flow with event buffering
  - Backup and restore flow
  - 100-200 concurrent websocket connections
- **Acceptance Criteria**:
  - Workflows pass end-to-end
  - No data loss on disconnect/reconnect
  - Permission errors are clear and actionable
  - Baseline concurrency is validated
  - Deployment steps are clear
- **Estimate**: 3 days
- **Owner**: Both

### Week 6 Acceptance
- [ ] Task 6.1 completed and pushed before Task 6.2 begins
- [ ] Appwrite backup can be created and restored
- [ ] Integration tests pass
- [ ] Baseline concurrency is validated
- [ ] Deployment runbook is ready

---

## Timeline Summary (6 Weeks, 12 Sequential Tasks)

| Week | Task 1 | Task 2 |
|---|---|---|
| 1 | 1.1 Appwrite Auth + Teams | 1.2 Board Metadata API |
| 2 | 2.1 Event Protocol | 2.2 Stroke Permission Validation |
| 3 | 3.1 IndexedDB Schema | 3.2 Websocket Client & Recovery |
| 4 | 4.1 Canvas Rendering Engine | 4.2 Stroke Ownership & Local Sync UI |
| 5 | 5.1 Incoming Stroke Application & Presence | 5.2 Appwrite Backup Table + API |
| 6 | 6.1 Backup Snapshot Flow and Restore | 6.2 Integration Testing, Baseline Scalability, Deployment Prep |

**Total**: 6 weeks to MVP launch with core collaboration features.

## Success Criteria for MVP

**Functional:**
- [ ] Users can create and join Appwrite teams
- [ ] Board metadata is stored separately from team management
- [ ] Multiple users can draw simultaneously on the same board
- [ ] Users can only edit/delete their own strokes
- [ ] Permission errors are surfaced clearly
- [ ] Backup can be created and restored for Pro teams
- [ ] Disconnection/reconnection works without data loss

**Non-Functional:**
- [ ] Supports 100-200 concurrent websocket connections
- [ ] Latency p95 < 200ms at baseline concurrency
- [ ] IndexedDB strokes persist locally
- [ ] Memory usage stays stable
- [ ] Browser compatibility across major browsers

**Security:**
- [ ] Authentication required for all APIs and websocket
- [ ] Appwrite permissions enforce multi-tenant isolation
- [ ] Server validates stroke mutations
- [ ] No unauthorized access to other teams' data

---

## Key Architectural Decisions (MVP Scope)

1. **Appwrite Teams Are Built-In**: Use Appwrite Auth + Teams/Memberships directly; no custom teams table.
2. **Local-First Storage**: Browser IndexedDB is the primary write path for strokes.
3. **Embedded Websocket**: ASP.NET Core System.Net.WebSockets; no external service.
4. **Event-Based Sync**: Individual stroke events broadcast immediately.
5. **Flat Canvas**: No layers; strokes are independent entities with creator ownership.
6. **Permission Model**: `createdBy` field plus server-side owner-only edit/delete enforcement.
7. **Appwrite Backup**: Pro users can back up board state through Appwrite in the MVP.
8. **Scalability Path**: Single instance MVP; Phase 2 adds load balancer + Redis pub/sub for 1000+ users.

---

## Phase 2 Roadmap (Post-MVP)

Future enhancements planned after MVP launch:

1. **Scalability**
   - Load balancer setup
   - Redis pub/sub for multi-instance broadcasting
   - Full 1000+ concurrent load testing

2. **Map Canvas & Tools**
   - Map layer (OSM tiles)
   - Advanced drawing tools
   - Layer management

3. **Advanced Features**
   - Real-time chat in board
   - Undo/redo
   - Templates and quick shapes
   - Viewer-only access
   - Scheduled backups

---

## Deployment Checklist

**Before MVP Launch:**
- [ ] Staging environment mirrors production
- [ ] Integration tests passing
- [ ] Baseline load testing (100-200 concurrent) passing
- [ ] Monitoring/alerting configured
- [ ] Rollback procedure documented
- [ ] Team trained on deployment steps
- [ ] Basic user documentation completed
- [ ] Error tracking active

**Day 1 Post-Launch:**
- [ ] Monitor error rates and latency
- [ ] Respond to critical issues within 2 hours
- [ ] Gather early user feedback
