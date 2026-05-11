# DrawSync Requirements Document

## Overview
This document serves as the single source of truth for the DrawSync platform's functional and non-functional requirements. It outlines the system's capabilities, performance targets, and constraints.

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

### 1.3 Drawing Sessions
- **Quantities**: Unlimited sessions per team.
- **Types**: `whiteboard` or `map`.
- **Metadata**: Name, creator, timestamp, archived flag.
- **Archiving**: Admins/Members can archive sessions (read-only mode).
- **Navigation**: Paginated list of sessions for all team members.

### 1.4 Whiteboard Canvas
- **Interface**: Infinite 2D canvas with panning (drag) and zooming (scroll/pinch).
- **Drawing**: vector-based freehand polylines with configurable color (hex), stroke width (px), and style (solid/dashed/dotted).
- **Persistence**: Completed strokes are saved immediately to Appwrite DB.
- **Real-Time**: Strokes from other participants appear via streaming as they are drawn.
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

### 1.7 Persistence
- **Storage**: Appwrite DB for polylines and placed symbols.
- **Lifecycle**: Content persists indefinitely until archived/deleted.
- **Loading**: Historical data must load before the canvas becomes interactive.

### 1.8 Layers
- **Organization**: Multiple named layers per session.
- **Association**: Every stroke/symbol belongs to exactly one layer.
- **Visibility**: Users can toggle layer visibility.
- **Management**: Admins/Members can add, rename, or delete layers (cascade delete content).

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
| **Scalability** | Up to 20 concurrent users per session. |
| **Security** | Authenticated APIs. Strict multi-tenant isolation. |
| **Availability** | 99.5% uptime target. |
| **Browser Support** | Chrome 110+, Firefox 110+, Edge 110+, Safari 16+. Basic mobile support. |
| **Accessibility** | WCAG 2.1 AA for non-canvas UI. |
| **Data Integrity** | Client-side retry logic for SignalR connection drops to prevent stroke loss. |

---

## 3. Out of Scope
- 3D rendering.
- Custom image/symbol uploads.
- Git-like version control/branching.
- Integrated video/voice chat.
- Offline mode.

---

## 4. Current Technical Status
- **Current State**: The project structure indicates an ASP.NET Core application with SQL Server (`ApplicationDbContext`).
- **Target State**: Migrate authentication and database persistence to Appwrite APIs.
- **Migration Plan**: Code logic will be updated to use Appwrite (data migration not required).
