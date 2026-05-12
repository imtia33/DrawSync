# DrawSync Implementation Roadmap

## Overview

This document outlines the phased implementation plan for DrawSync, broken into milestones with concrete deliverables and estimated effort.

---

## Phase 1: Foundation & Authentication (Weeks 1-3)

### Goal
Set up Appwrite authentication, team/membership management, and basic API structure.

### Deliverables

#### 1.1 Appwrite Setup & Teams Table
- **Task**: Create Appwrite database schema (teams, team_members, users tables)
- **Acceptance Criteria**:
  - Teams table created with: name, planTier, boardLimit, seatLimit, extraSeatPacks, backupEnabled
  - Team_members table with: teamID, userID, role (admin/member/viewer)
  - Users table linked to Appwrite Auth
- **Dependencies**: Appwrite project already set up
- **Estimate**: 3 days
- **Owner**: Backend

#### 1.2 Team API Endpoints
- **Task**: Implement REST endpoints for team CRUD and member management
- **Endpoints**:
  - `POST /api/teams` - Create team (user becomes admin)
  - `GET /api/teams` - List teams for current user
  - `GET /api/teams/{teamId}` - Get team details + plan info
  - `POST /api/teams/{teamId}/members` - Invite member (admin only)
  - `DELETE /api/teams/{teamId}/members/{userId}` - Remove member
  - `PATCH /api/teams/{teamId}/members/{userId}` - Change role
- **Acceptance Criteria**:
  - All endpoints enforce authentication
  - Plan tier checked for entitlements (board/seat limits)
  - Appwrite RLS used for multi-tenant isolation
- **Estimate**: 5 days
- **Owner**: Backend

#### 1.3 Plan Enforcement Service
- **Task**: Create PlanService to check board/seat limits
- **Methods**:
  - `CanCreateBoard(teamId)` - Returns true if boardCount < limit
  - `CanAddMember(teamId)` - Returns true if memberCount < limit
  - `GetPlanLimits(teamId)` - Returns { boardLimit, seatLimit }
- **Estimate**: 2 days
- **Owner**: Backend

### Milestone Acceptance
- [ ] All team endpoints tested and working
- [ ] PlanService enforces Free (1 board, 3 members) and Pro (5 boards, 15 members) limits
- [ ] Multi-tenant isolation verified (user A cannot see team B's data)

---

## Phase 2: Boards & Metadata (Weeks 2-4)

### Goal
Implement board CRUD, persistence in Appwrite, and local browser DB setup.

### Deliverables

#### 2.1 Boards Table & API
- **Task**: Create Appwrite boards table and REST API
- **Appwrite Schema**:
  - `boards` table: teamID, name, type (whiteboard/map), archived, createdBy, createdAt
- **Endpoints**:
  - `POST /api/teams/{teamId}/boards` - Create board (enforces limit)
  - `GET /api/teams/{teamId}/boards` - List boards (paginated)
  - `GET /api/teams/{teamId}/boards/{boardId}` - Get board metadata
  - `PATCH /api/teams/{teamId}/boards/{boardId}` - Update name/archive status
  - `DELETE /api/teams/{teamId}/boards/{boardId}` - Delete board
- **Acceptance Criteria**:
  - Board count checked against plan limit before creation
  - Archived boards return 403 on edit attempts
  - Delete cascades to layers, strokes in local DB (client-side)
- **Estimate**: 4 days
- **Owner**: Backend

#### 2.2 IndexedDB Setup (Client)
- **Task**: Design and initialize IndexedDB schema for local persistence
- **Stores**: boards, layers, strokes, symbols, backups (see architecture doc)
- **Acceptance Criteria**:
  - IndexedDB "DrawSync" database created on first app load
  - Schema versioning implemented (for future migrations)
  - All stores support async read/write
  - Compound indices created for fast queries (boardId, layerId, etc.)
- **Estimate**: 3 days
- **Owner**: Frontend

#### 2.3 Board Metadata Sync (Client)
- **Task**: Sync boards/layers from Appwrite to local IndexedDB on login
- **Flow**:
  - On login, fetch teams from Appwrite
  - For each team, fetch boards + layers
  - Insert into local IndexedDB
  - Set up periodic refresh (every 5 minutes or on app focus)
- **Acceptance Criteria**:
  - Boards appear in UI after login
  - Adding board via API reflects in local DB within 30s
  - Archiving board disables editing in UI
- **Estimate**: 3 days
- **Owner**: Frontend

### Milestone Acceptance
- [ ] User can create board (within plan limit)
- [ ] Board appears in local IndexedDB
- [ ] Boards synced from Appwrite to local DB
- [ ] Archived boards cannot be edited

---

## Phase 3: Websocket Server (Weeks 4-6)

### Goal
Implement in-process websocket server for real-time collaboration.

### Deliverables

#### 3.1 Websocket Infrastructure
- **Task**: Implement websocket server in ASP.NET Core
- **Components**:
  - WebsocketSession class (manages board session state)
  - WebsocketClient class (per-connection state)
  - Event buffer (last 1000 events per session)
  - Connection manager (track active sessions)
- **Acceptance Criteria**:
  - Websocket endpoint `/ws` accepts connections
  - Connections validated with auth token
  - Session created per board
  - Heartbeat sent every 5s to detect disconnections
- **Estimate**: 5 days
- **Owner**: Backend

#### 3.2 Event Serialization & Broadcasting
- **Task**: Implement event protocol (stroke, layerCreate, cursorMove, etc.)
- **Events to implement**:
  - `stroke` - New stroke from client
  - `strokeUpdate` - Update existing stroke
  - `cursorMove` - Presence indicator
  - `layerCreate` - New layer
  - `layerDelete` - Delete layer
  - `presence` - Join/leave broadcast
  - `snapshot` - Full board state on join
  - `ack` - Acknowledgment
- **Acceptance Criteria**:
  - Events serialized as JSON with schema version
  - Broadcast to all clients in session within 50ms
  - Server validates event before relay
  - ACK sent to sender
- **Estimate**: 5 days
- **Owner**: Backend

#### 3.3 Connection Management & Recovery
- **Task**: Implement reconnection & event buffer logic
- **Features**:
  - Detect disconnection (no heartbeat > 30s)
  - Remove client from session gracefully
  - Buffer events for reconnecting clients
  - Send snapshot + pending events on reconnect
- **Acceptance Criteria**:
  - Client reconnects within 10s after network drop
  - No events lost (buffered or resent)
  - Snapshot sent to new joiners
  - Memory usage bounded (1000 events max per session)
- **Estimate**: 4 days
- **Owner**: Backend

#### 3.4 Websocket Client Library
- **Task**: Create client-side library to handle websocket events
- **Features**:
  - Auto-reconnect with exponential backoff
  - Queue events during disconnection
  - Listen for incoming events
  - Manage local optimistic updates
- **Methods**:
  - `connect(boardId, token)` - Start websocket
  - `send(event)` - Send stroke/layer event
  - `on(eventType, handler)` - Listen for events
  - `disconnect()` - Close gracefully
- **Acceptance Criteria**:
  - Events sent successfully
  - Incoming events trigger UI updates
  - Reconnects after 5s disconnect
  - No race conditions with optimistic updates
- **Estimate**: 3 days
- **Owner**: Frontend

### Milestone Acceptance
- [ ] Two clients connect to same board
- [ ] Client A sends stroke; Client B receives it within 100ms
- [ ] Client A disconnects; Client B still connected
- [ ] Client A reconnects; receives buffered events
- [ ] Full board snapshot sent on new client join

---

## Phase 4: Canvas & Drawing (Weeks 5-8)

### Goal
Implement interactive canvas and drawing UI.

### Deliverables

#### 4.1 Canvas Rendering Engine
- **Task**: Implement whiteboard & map canvas with pan/zoom
- **Features**:
  - Infinite 2D canvas (whiteboard)
  - OSM tile layer (map)
  - Pan (drag) and zoom (scroll/pinch)
  - Layer visibility toggle
  - Freehand drawing with brush stroke
- **Acceptance Criteria**:
  - Canvas renders strokes from local DB
  - Pan/zoom works smoothly
  - Brush strokes drawn in real-time
  - Layers can be toggled on/off
- **Estimate**: 6 days
- **Owner**: Frontend

#### 4.2 Local Stroke Persistence
- **Task**: Save strokes to local IndexedDB as they are drawn
- **Flow**:
  - User draws stroke on canvas
  - On stroke end, save to IndexedDB strokes table
  - Emit stroke event to websocket (async)
  - UI updates from local DB
- **Acceptance Criteria**:
  - Stroke persisted to IndexedDB
  - Stroke appears in local DB after save
  - Stroke sent to websocket
  - UI does not block on save
- **Estimate**: 3 days
- **Owner**: Frontend

#### 4.3 Incoming Stroke Application
- **Task**: Apply incoming websocket strokes to local DB and canvas
- **Flow**:
  - Receive stroke event from websocket
  - Insert into local IndexedDB (if not from self)
  - Render stroke on canvas
  - Update canvas view
- **Acceptance Criteria**:
  - Strokes from other clients appear on canvas
  - Local DB updated with remote strokes
  - No duplicate rendering (optimization)
  - Latency < 100ms p95
- **Estimate**: 3 days
- **Owner**: Frontend

#### 4.4 Presence & Cursors
- **Task**: Display live cursors from other users
- **Features**:
  - Cursor position sent via websocket every ~66ms (15 fps)
  - Other users' cursors rendered on canvas
  - Name label displayed above cursor
  - Cursor removed on user disconnect
- **Acceptance Criteria**:
  - Cursor updates sent without blocking drawing
  - Cursors from other clients visible
  - Smooth movement (no jittering)
  - Cursor removed 1s after user disconnect
- **Estimate**: 2 days
- **Owner**: Frontend

### Milestone Acceptance
- [ ] Two users can draw simultaneously
- [ ] Both see each other's strokes within 100ms
- [ ] Strokes persisted locally
- [ ] Layers can be created and toggled
- [ ] Cursors visible for both users

---

## Phase 5: Cloud Backup (Weeks 7-9)

### Goal
Implement Pro-tier cloud backup and restore functionality.

### Deliverables

#### 5.1 Board Backups Table & API
- **Task**: Create board_backups table in Appwrite and REST endpoints
- **Appwrite Schema**:
  - `board_backups` table: boardID, version, payload, checksum, createdBy, createdAt
- **Endpoints**:
  - `POST /api/teams/{teamId}/boards/{boardId}/backup` - Create backup snapshot
  - `GET /api/teams/{teamId}/boards/{boardId}/backups` - List backup versions
  - `POST /api/teams/{teamId}/boards/{boardId}/restore?version=X` - Restore from backup
- **Acceptance Criteria**:
  - Only Pro tier teams can create backups (403 if Free)
  - Backups include full board state (layers, strokes, symbols)
  - Checksum validated on restore
  - Multiple backup versions tracked
- **Estimate**: 4 days
- **Owner**: Backend

#### 5.2 Backup Snapshot Creation
- **Task**: Implement snapshot serialization from local DB
- **Flow**:
  - Client collects all layers, strokes, symbols for board
  - Serialize to JSON
  - Compute checksum (SHA256)
  - POST to `/api/teams/{teamId}/boards/{boardId}/backup`
- **Acceptance Criteria**:
  - Snapshot includes all drawing data
  - Checksum computed correctly
  - Payload size reasonable (~1-10MB for typical board)
  - Snapshot sent in < 5 seconds
- **Estimate**: 2 days
- **Owner**: Frontend

#### 5.3 Backup Trigger UI
- **Task**: Add "Save to Cloud" button in UI (Pro tier only)
- **Features**:
  - Button visible only for Pro teams
  - Button disabled during backup
  - Show progress indicator
  - Show success/error notification
- **Acceptance Criteria**:
  - Button triggers backup
  - UI shows saving state
  - Success notification after backup
  - Error notification if backup fails
  - Button disabled for Free tier
- **Estimate**: 2 days
- **Owner**: Frontend

#### 5.4 Restore from Backup
- **Task**: Implement restore flow
- **Flow**:
  - User opens "Backups" panel
  - See list of versions with timestamps
  - Click "Restore" on a version
  - Confirmation dialog
  - Clear local board, insert snapshot data
  - Canvas re-renders
- **Acceptance Criteria**:
  - Backup list displays versions
  - Restore clears local board (with confirmation)
  - Snapshot data loaded correctly
  - Canvas re-renders with restored data
  - Other connected users notified of restore
- **Estimate**: 3 days
- **Owner**: Frontend

### Milestone Acceptance
- [ ] Pro user can save board to cloud
- [ ] Backup appears in Appwrite
- [ ] Backup can be restored
- [ ] Free tier blocked from backup feature
- [ ] Multiple backup versions tracked

---

## Phase 6: Testing & Load (Weeks 8-10)

### Goal
Comprehensive testing and load testing to validate 1000 concurrent user capability.

### Deliverables

#### 6.1 Unit Tests
- **Task**: Write unit tests for services and domain logic
- **Coverage**:
  - PlanService (board/seat limit checks)
  - WebsocketSession (event buffering, broadcast)
  - Board/Team services (CRUD, entitlement)
- **Acceptance Criteria**:
  - >80% code coverage
  - All tests pass
  - CI/CD integrated
- **Estimate**: 5 days
- **Owner**: Both

#### 6.2 Integration Tests
- **Task**: Test end-to-end workflows
- **Scenarios**:
  - Create team → Create board → Join → Draw → Save
  - Multi-client drawing → Verify consistency
  - Backup → Restore → Verify data
- **Acceptance Criteria**:
  - All workflows pass
  - No data loss
  - Consistency verified across clients
- **Estimate**: 4 days
- **Owner**: Both

#### 6.3 Load Testing
- **Task**: Test 1000 concurrent websocket connections
- **Setup**: Use k6 or similar tool
- **Scenarios**:
  - 1000 clients connect
  - 100 clients per session (10 sessions)
  - Each client sends stroke every 1-2 seconds
  - Measure latency, CPU, memory
- **Acceptance Criteria**:
  - No connection failures
  - Latency p95 < 100ms
  - Memory usage stable
  - CPU usage < 80%
  - No packet loss
- **Estimate**: 4 days
- **Owner**: Backend

#### 6.4 Reliability & Recovery Tests
- **Task**: Test connection drops, server restarts, etc.
- **Scenarios**:
  - Client disconnects mid-stroke
  - Server restart (graceful drain)
  - Client reconnects after 1 minute offline
  - Appwrite downtime (cache local state)
- **Acceptance Criteria**:
  - No data loss
  - Clients recover after restart
  - Graceful handling of offline periods
- **Estimate**: 3 days
- **Owner**: Backend

### Milestone Acceptance
- [ ] Load test passes: 1000 concurrent connections, p95 < 100ms
- [ ] All integration tests pass
- [ ] Recovery tests pass (no data loss)
- [ ] Performance monitoring in place

---

## Phase 7: Polish & Deployment (Weeks 9-11)

### Goal
UI/UX refinement, documentation, and production deployment.

### Deliverables

#### 7.1 UI/UX Polish
- **Task**: Refine UI, add animations, improve mobile support
- **Features**:
  - Responsive layout (mobile, tablet, desktop)
  - Smooth animations for presence cursors
  - Notification system (join/leave, errors)
  - Accessibility audit (WCAG 2.1 AA)
- **Acceptance Criteria**:
  - UI looks polished
  - Animations smooth (60 fps)
  - Mobile usable
  - Accessibility passing
- **Estimate**: 5 days
- **Owner**: Frontend

#### 7.2 Documentation
- **Task**: Write developer/user documentation
- **Docs**:
  - API documentation (Swagger/OpenAPI)
  - Architecture guide (this doc)
  - Deployment guide
  - User guide (getting started)
- **Acceptance Criteria**:
  - All APIs documented
  - Setup steps clear
  - User workflows explained
- **Estimate**: 3 days
- **Owner**: Both

#### 7.3 Deployment & Monitoring
- **Task**: Deploy to production and set up monitoring
- **Components**:
  - App Insights or similar monitoring
  - Error tracking (Sentry or similar)
  - Logging (ELK or Application Insights)
  - Alerting (connection count, error rate, latency)
- **Acceptance Criteria**:
  - App deployed to production
  - Monitoring dashboards active
  - Alerts configured
  - Runbook prepared
- **Estimate**: 3 days
- **Owner**: DevOps/Backend

#### 7.4 Performance Optimization
- **Task**: Profile and optimize based on load test findings
- **Focus Areas**:
  - Websocket memory usage
  - Event buffer cleanup
  - Appwrite query optimization
  - Client-side rendering performance
- **Acceptance Criteria**:
  - Memory stable under load
  - Latency within target
  - CPU usage reduced if possible
- **Estimate**: 3 days
- **Owner**: Backend

### Milestone Acceptance
- [ ] App deployed to production
- [ ] Monitoring active and alerting working
- [ ] UI polished and responsive
- [ ] Documentation complete
- [ ] Performance optimized

---

## Phase 8: Beta & Feedback (Weeks 11-12)

### Goal
Beta launch with early users, collect feedback, and iterate.

### Deliverables

#### 8.1 Beta User Onboarding
- **Task**: Set up beta program and onboard first users
- **Features**:
  - Invite beta testers
  - Collect feedback (survey, in-app feedback)
  - Monitor for issues
- **Estimate**: 2 days
- **Owner**: Product

#### 8.2 Issue Triage & Fixes
- **Task**: Prioritize and fix beta feedback issues
- **Process**:
  - Daily triage of reported issues
  - P1 (critical) fixes within 24h
  - P2 (major) fixes within 3 days
- **Estimate**: 5 days
- **Owner**: Both

### Milestone Acceptance
- [ ] 50+ beta users active
- [ ] Major issues fixed
- [ ] Positive feedback on core features

---

## Timeline Summary

| Phase | Duration | Key Deliverables |
|---|---|---|
| 1 | Weeks 1-3 | Auth, Teams, Plan enforcement |
| 2 | Weeks 2-4 | Boards, IndexedDB, Metadata sync |
| 3 | Weeks 4-6 | Websocket server, Event protocol |
| 4 | Weeks 5-8 | Canvas, Drawing, Layer management |
| 5 | Weeks 7-9 | Cloud backup, Restore |
| 6 | Weeks 8-10 | Testing, Load testing |
| 7 | Weeks 9-11 | Polish, Deployment, Monitoring |
| 8 | Weeks 11-12 | Beta, Feedback, Iterations |

**Total**: ~12 weeks (3 months) to MVP with beta launch.

---

## Resource Plan

| Role | Weeks 1-6 | Weeks 7-12 |
|---|---|---|
| **Backend** | 1 FTE (websocket, API, backup) | 0.5 FTE (optimization, support) |
| **Frontend** | 0.5 FTE (setup, structure) → 1 FTE | 1 FTE (UI/UX, testing) |
| **DevOps** | 0.2 FTE (infrastructure) | 0.5 FTE (deployment, monitoring) |

---

## Risk & Mitigation

| Risk | Impact | Mitigation |
|---|---|---|
| Websocket scaling challenges | High | Early load testing (Phase 6); consider Redis pub/sub if needed |
| IndexedDB performance (millions of rows) | High | Implement pagination; monitor query times; SQLite migration ready |
| Appwrite API rate limits | Medium | Monitor usage; implement caching; request quota increase |
| Browser compatibility | Medium | Test on Chrome, Firefox, Safari, Edge; use polyfills as needed |
| Team member coordination | Low | Daily standup; clear ownership; Slack integration for updates |

