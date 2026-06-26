using System.Collections.Concurrent;

namespace DrawSync.Hubs
{
    /// <summary>
    /// In-memory realtime event debugger. Captures SignalR drawing-hub events (join/leave/broadcast)
    /// so we can verify that broadcasts are correctly restricted to team members.
    ///
    /// Events are kept in a bounded ring buffer and exposed via /api/debug/realtime and an on-board
    /// debug panel. This is a development/diagnostics aid — it logs connection ids, user ids, the
    /// target drawing/org, the membership decision and the broadcast recipient count.
    /// </summary>
    public static class RealtimeDebugger
    {
        public enum Level { Info, Warn, Error }

        public sealed class DebugEvent
        {
            public long TimestampMs { get; set; }
            public DateTime TimestampUtc { get; set; }
            public string Level { get; set; } = "Info";
            public string Category { get; set; } = "";
            public string Message { get; set; } = "";
            public string? ConnectionId { get; set; }
            public string? UserId { get; set; }
            public string? DrawingId { get; set; }
            public string? OrganizationId { get; set; }
            public string? Action { get; set; }
            public int RecipientCount { get; set; }
            public bool MembershipOk { get; set; }
        }

        public sealed class RoomSnapshot
        {
            public string DrawingId { get; set; } = "";
            public string? OrganizationId { get; set; }
            public int ConnectionCount { get; set; }
            public List<PresenceEntry> Connections { get; set; } = new();
        }

        public sealed class PresenceEntry
        {
            public string ConnectionId { get; set; } = "";
            public string UserId { get; set; } = "";
            public string UserName { get; set; } = "";
            public string Color { get; set; } = "";
        }

        private const int MaxEvents = 500;
        private static readonly ConcurrentQueue<DebugEvent> _events = new();
        private static long _eventCounter;

        // Current room presence, mirrored from the hub so the debug API can report it without
        // reaching into hub internals.
        private static readonly ConcurrentDictionary<string, RoomSnapshot> _rooms = new();

        public static List<DebugEvent> GetRecentEvents(int count = 100)
        {
            return _events.Reverse().Take(count).ToList();
        }

        public static List<RoomSnapshot> GetRooms()
        {
            return _rooms.Values.ToList();
        }

        public static long TotalEvents => Interlocked.Read(ref _eventCounter);

        public static void Log(string category, string message, Level level = Level.Info,
            string? connectionId = null, string? userId = null, string? drawingId = null,
            string? organizationId = null, string? action = null,
            int recipientCount = 0, bool membershipOk = false)
        {
            var evt = new DebugEvent
            {
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TimestampUtc = DateTime.UtcNow,
                Level = level.ToString(),
                Category = category,
                Message = message,
                ConnectionId = connectionId,
                UserId = userId,
                DrawingId = drawingId,
                OrganizationId = organizationId,
                Action = action,
                RecipientCount = recipientCount,
                MembershipOk = membershipOk
            };

            _events.Enqueue(evt);
            Interlocked.Increment(ref _eventCounter);
            while (_events.Count > MaxEvents)
            {
                _events.TryDequeue(out _);
            }

            // Also emit to console for live `dotnet run` observation.
            var prefix = level switch
            {
                Level.Warn => "[RT-WARN]",
                Level.Error => "[RT-ERROR]",
                _ => "[RT-INFO]"
            };
            Console.WriteLine($"{prefix} {category} | {message} | conn={connectionId?.Substring(0, Math.Min(8, connectionId?.Length ?? 0))} user={userId} drawing={drawingId} org={organizationId} recipients={recipientCount} membership={membershipOk}");
        }

        public static void UpsertRoom(string drawingId, string? organizationId, IEnumerable<PresenceEntry> connections)
        {
            var snap = new RoomSnapshot
            {
                DrawingId = drawingId,
                OrganizationId = organizationId,
                Connections = connections.ToList()
            };
            snap.ConnectionCount = snap.Connections.Count;
            _rooms[drawingId] = snap;
        }

        public static void RemoveRoom(string drawingId)
        {
            _rooms.TryRemove(drawingId, out _);
        }
    }
}
