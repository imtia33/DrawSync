# DrawSync Requirements Document

## Overview
This document serves as the single source of truth for the DrawSync platform's functional and non-functional requirements. It outlines the system's capabilities, performance targets, and constraints.

The platform is being redesigned around a tiered storage model to reduce cloud cost. Free users keep drawing data in the browser's local database, while Pro users get the same local database experience plus optional cloud backup.

---

## 1. Functional Requirements

### 1.1 Authentication & Users
- **Registration**: Users must register with email and password via Appwrite Auth.
- **OAuth**: Support for Google and GitHub login as alternatives.
- **Verification**: Email verification is mandatory before joining or creating a team.
- **Password Management**: Users can reset passwords via an email link.
- **User Profile**: Stores display name, avatar URL, and created-at timestamp.
- **Note**: Currently transitioning from SQL Server Authentication to Appwrite API.

### 1.2 Team Management
- **Ownership**: Creating a team automatically assigns the user as **Admin**.
- **Invitations**: Admins can invite members via email; invitees receive a link.
- **Roles**:
  - **Admin**: Full control over team, members, and sessions.
  - **Member**: Can create sessions and draw.
  - **Viewer**: Read-only access to sessions.
- **Member Management**: Admins can modify roles or remove members.
- **Isolation**: Team data is strictly private to the team.
- **Plans**: Plans are assigned at the team level, not per individual user.
  - **Free**: 1 board per team, any board type, unlimited rows, and up to 3 teammates/viewers on that board. Data stays in the browser local database.
  - **Pro**: $10.99 per month for 5 boards and up to 15 teammates/viewers. Data stays in the browser local database and can also be backed up to the cloud.
  - **Additional Seats**: $5 per extra pack of 5 teammates/viewers.
  - **Backup Access**: Team members allowed by role can back up and restore the shared team backup set.

### 1.3 Drawing Sessions
- **Quantities**: Board count is limited by the team plan tier.
- **Types**: `whiteboard` or `map`.
- **Metadata**: Name, creator, timestamp, archived flag.
- **Archiving**: Admins/Members can archive sessions (read-only mode).
- **Navigation**: Paginated list of sessions for all team members.

### 1.4 Whiteboard Canvas
- **Interface**: Infinite 2D canvas with panning (drag) and zooming (scroll/pinch).
- **Drawing**: vector-based freehand polylines with configurable color (hex), stroke width (px), and style (solid/dashed/dotted).
- **Persistence**: The canvas uses a browser local database as the primary write path for all users.
- **Plan Tier Behavior**:
  - **Free**: Drawing data remains local to the browser unless the product later adds an upgrade path.
  - **Pro**: Drawing data is stored locally and can be backed up to cloud storage when the user presses Save, or on another configured backup interval.
- **Real-Time**: Strokes from other participants appear via the project-owned websocket layer as they are drawn.
- **Presence**: Live cursors with participant name labels.

### 1.5 Map Drawboard
- **Engine**: OpenStreetMap tile layer rendered via Leaflet.js.
- **Drawing**: Freehand polylines stored as geographic lat/lng arrays.
- **Measurement**: Real-time display of distance in meters (< 1 km) or kilometers.
- **Symbols**: Placement of predefined construction symbols:
  - Bridge, Building, Excavation, Crane, Road Work, Pipe, Electric Pole.
- **Controls**: Scale bar always visible; standard Leaflet zoom/pan behavior.
- **Constraints**: No custom symbol/image uploads.

### 1.6 Real-Time Collaboration
- **Latency**: Stroke events delivered within **100 ms** (p95 target on LAN).
- **Events**: Broadcasts for participants joining/leaving.
- **Presence**: Cursor updates at ≥ 15 fps.
- **Transport**: Use a websocket implementation built into this project rather than Appwrite Realtime or a third-party realtime service.

### 1.7 Persistence
- **Storage**: Browser local database for all drawing rows by default.
- **Cloud Backup**: Pro users can back up local drawing data to the cloud on Save or during a scheduled sync.
- **Lifecycle**: Local data persists in the browser until cleared by the user or browser storage policies; cloud backups persist until archived/deleted.
- **Loading**: Historical data must load from the browser local database before the canvas becomes interactive.



### 1.9 Export
- **Map Sessions**: Export as GeoJSON FeatureCollection.
- **Whiteboard Sessions**: Export visible canvas or full extents as PNG.

### 1.10 Audit Log (Optional/Compliance)
- **Tracking**: User ID, timestamp, and action type for every write.
- **Access**: Dedicated UI panel for Admins.

---

## 2. Non-Functional Requirements

| Category | Requirement |
|---|---|
| **Performance** | Stroke latency < 100 ms (p95 LAN). Page load < 3s on 4G. |
| **Scalability** | Support at least 1,000 concurrent users at launch across the server and websocket layer, with session-level collaboration remaining responsive. |
| **Security** | Authenticated APIs. Strict multi-tenant isolation. |
| **Availability** | 99.5% uptime target. |
| **Browser Support** | Chrome 110+, Firefox 110+, Edge 110+, Safari 16+. Basic mobile support. |
| **Accessibility** | WCAG 2.1 AA for non-canvas UI. |
| **Data Integrity** | Client-side retry and reconciliation logic for websocket connection drops to prevent stroke loss and to keep local and cloud copies consistent. |

---

## 3. Out of Scope
- 3D rendering.
- Custom image/symbol uploads.
- Git-like version control/branching.
- Integrated video/voice chat.
- Offline mode.

---

## 4. Current Technical Status
- **Current State**: The project is an ASP.NET Core application and already acts as part of the server stack for DrawSync.
- **Target State**: Use Appwrite TablesDB for cloud-backed account and drawing backup data where needed, while keeping the browser local database as the default write path.
- **Realtime Plan**: Implement an in-project websocket service for collaboration instead of Appwrite Realtime.
- **Migration Plan**: Update the application logic around tiered storage, local persistence, cloud backup, and websocket sync; historical migration of prior data is not required.
