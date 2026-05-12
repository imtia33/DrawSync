# Appwrite Database Documentation

This document describes the Appwrite TablesDB schema used for DrawSync's cloud control plane and backup storage. The browser local database remains the primary store for live drawing rows; Appwrite is used for identity, team entitlements, and cloud backup snapshots.

## Database Structure

The project uses a single database (ID: `drawsync`) containing the following tables.

### 1. Users (`users`)
Stores the Appwrite-linked user profile.

| Column | Type | Description |
| :--- | :--- | :--- |
| `name` | String | User's display name |
| `email` | String | User's email address |
| `avatarUrl` | String | Optional profile image URL |

*Notes:*
- The `$id` of the row should ideally match the Appwrite Account ID.
- Use `Query.Equal("email", email)` for lookups if the ID isn't known.

### 2. Teams (`teams`)
Represents a shared drawing workspace and its subscription state.

| Column | Type | Description |
| :--- | :--- | :--- |
| `name` | String | Team name |
| `planTier` | Enum | `free` or `pro` |
| `boardLimit` | Integer | Number of boards allowed for the team |
| `seatLimit` | Integer | Number of teammates/viewers included in the plan |
| `extraSeatPacks` | Integer | Number of purchased 5-seat add-ons |
| `backupEnabled` | Boolean | Whether cloud backup is available for the team |

### 3. Team Members (`team_members`)
Maps users to teams and roles.

| Column | Type | Description |
| :--- | :--- | :--- |
| `teamID` | String | Parent team |
| `userID` | String | Appwrite user ID |
| `role` | Enum | `admin`, `member`, or `viewer` |

### 4. Boards (`boards`)
Stores board-level metadata. The live canvas data itself stays in the browser local database.

| Column | Type | Description |
| :--- | :--- | :--- |
| `teamID` | String | Parent team |
| `name` | String | Board name |
| `type` | Enum | `whiteboard` or `map` |
| `archived` | Boolean | Read-only flag |
| `createdBy` | String | User ID of the creator |

### 5. Board Backups (`board_backups`)
Stores cloud backup snapshots that can be restored by team members with permission.

| Column | Type | Description |
| :--- | :--- | :--- |
| `boardID` | String | Parent board |
| `version` | Integer | Snapshot version number |
| `payload` | String | Stringified JSON snapshot of the board data |
| `checksum` | String | Integrity hash for restore validation |
| `createdBy` | String | User ID that created the backup |
| `createdAt` | String | Timestamp of backup creation |

---

## Implementation Guidelines (C# / .NET)

### Serialization Pattern
As noted in [docs/appwrite-findings.md](docs/appwrite-findings.md), `Row.Data` must be serialized before deserialization:

```csharp
var json = JsonConvert.SerializeObject(row.Data);
var model = JsonConvert.DeserializeObject<T>(json);
```

### Model Decoration
Ensure C# models handle Appwrite system attributes:

```csharp
public class AppwriteBaseModel {
    [JsonProperty("$id")]
    public string Id { get; set; }
    
    [JsonProperty("$createdAt")]
    public string CreatedAt { get; set; }
}
```

### Data Mutations
When creating or updating rows, remove system attributes (`$id`, `$createdAt`, etc.) from the dictionary payload to avoid API errors.
