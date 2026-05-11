# Appwrite Database Documentation

This document describes the schema and usage of Appwrite as the primary database for DrawSync. Based on the `appwrite-findings.md`, we use the `TablesDB` service.

## Database Structure

The project uses a single database (ID: `drawsync`) containing the following tables.

### 1. Users (`users`)
Stores basic user profile information.

| Column | Type | Description |
| :--- | :--- | :--- |
| `name` | String | User's full name |
| `email` | String | User's email address (used for lookup) |

*Notes:* 
- The `$id` of the row should ideally match the Appwrite Account ID.
- Use `Query.Equal("email", email)` for lookups if the ID isn't known.

### 2. Drawings (`drawings`)
Containers for different drawing projects.

| Column | Type | Description |
| :--- | :--- | :--- |
| `name` | String | The display name of the drawing |

### 3. Map (`map`)
Stores geospatial data layers and polylines.

| Column | Type | Description |
| :--- | :--- | :--- |
| `drawingID` | String | Pointer to the parent drawing ID |
| `attributes` | String | Visual metadata (color, thickness, etc.).is saved as stringified json. |
| `type` | Enum | `geometry` or `custom` (text/icon) |
| `geometry` | line | Coordinate data in format `[[x1,y1],[x2,y2]]` |

### 4. Whiteboard (`whiteboard`)
Stores whiteboard elements. Matches the Map schema but used for free-form whiteboarding.

| Column | Type | Description |
| :--- | :--- | :--- |
| `drawingID` | Relationship / String | Pointer to the parent drawing ID |
| `attributes` | String | Visual metadata (color, thickness, etc.).is saved as stringified json. |
| `type` | Enum | `geometry` or `custom` |
| `geometry` | line | Element coordinates |

### 5. Allowance (`allowence`)
Tracks resource quotas for users.

| Column | Type | Description |
| :--- | :--- | :--- |
| `userID` | String | Pointer to the User ID |
| `drawings` | Integer | current drawing count |
| `rows` | Integer | current rows count |

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
