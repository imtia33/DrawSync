# Appwrite .NET SDK - TablesDB Implementation Findings

This document outlines key technical findings and solutions discovered during the migration of DrawSync from local SQL to Appwrite TablesDB.

## 1. Terminology Migration
Appwrite's latest API uses `TablesDB` (Databases -> Tables -> Rows) instead of the older `Databases` (Databases -> Collections -> Documents) terminology found in many community guides.
- **Service**: Use `Appwrite.Services.TablesDB`.
- **Methods**: Use `ListRows`, `GetRow`, `CreateRow`, `UpdateRow`, and `DeleteRow`.

## 2. Row Data Serialization (Critical)
When fetching data using `GetRow` or `ListRows`, the `Row.Data` property in the .NET SDK is returned as a complex object (type `object`), not a raw JSON string.

### The Problem
Calling `row.Data.ToString()` returns the class name or a non-JSON string (e.g., `System.Collections.Generic.Dictionary...`), which causes `Newtonsoft.Json` to fail with:
> "Unexpected character encountered while parsing value: S. Path '', line 0, position 0."

### The Solution
Always serialize the `Data` object back to JSON before deserializing it into your C# models:
```csharp
var json = JsonConvert.SerializeObject(row.Data);
var model = JsonConvert.DeserializeObject<T>(json);
```

## 3. ID and System Attributes
To keep C# models identical to Appwrite cloud objects, decoration with `JsonProperty` is required for system attributes:
```csharp
[JsonProperty("$id")]
public string Id { get; set; }

[JsonProperty("$createdAt")]
public string CreatedAt { get; set; }

[JsonProperty("$updatedAt")]
public string UpdatedAt { get; set; }
```
**Note**: `CreatedAt` and `UpdatedAt` should be handled as `string` types to match the ISO 8601 strings returned by the SDK, rather than `DateTime`, to ensure error-free direct mapping.

## 4. Authentication and Permissions (RLS)
When using Row Level Security (RLS) on Appwrite Tables:
- **Registration**: You must create a session (`CreateEmailPasswordSession`) *immediately after* account creation, but *before* adding the user to the `users` table. TablesDB requires an active session to verify "Users" role permissions for the write operation.
- **Fetching**: Use `ListRows` with a query (e.g., `Query.Equal("email", email)`) if you cannot guarantee that the Document ID matches the Account ID.

## 5. Cleaning Objects for Upsert
Appwrite will reject `CreateRow` or `UpdateRow` calls if the data object contains system-generated fields like `$id`, `$createdAt`, or `$updatedAt`. These must be removed from the payload dictionary before being sent to the server.
