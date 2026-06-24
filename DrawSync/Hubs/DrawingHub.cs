using DrawSync.Repositories.Interface;
using DrawSync.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace DrawSync.Hubs
{
    public class ConnectionInfo
    {
        public string ConnectionId { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public string Color { get; set; } = null!;
        public string BoardType { get; set; } = "whiteboard";
        public string? OrganizationId { get; set; }
        public string DrawingId { get; set; } = null!;
    }

    public class DrawingRoom
    {
        public string DrawingId { get; set; } = null!;
        public string? OrganizationId { get; set; }
        public List<ConnectionInfo> Connections { get; set; } = new();
        public object Lock { get; } = new();
    }

    /// <summary>
    /// Realtime drawing collaboration hub.
    ///
    /// SECURITY: A connection may only join a drawing's group after the server verifies that the
    /// authenticated user is a member of the Appwrite Team that owns the drawing's organization.
    /// Because broadcasts target `OthersInGroup(drawingId)`, only verified team members present in
    /// the drawing ever receive them.
    ///
    /// All join/leave/broadcast events are recorded via <see cref="RealtimeDebugger"/> so the
    /// behavior can be inspected live (console + /api/debug/realtime + on-board debug panel).
    /// </summary>
    [Authorize]
    public class DrawingHub : Hub
    {
        // Max users per drawing
        private const int MaxUsersPerDrawing = 5;

        // Color palette for user cursors (5 distinct, accessible colors)
        private static readonly string[] UserColors = new[]
        {
            "#3b82f6", // Blue
            "#ef4444", // Red
            "#10b981", // Green
            "#f59e0b", // Amber
            "#8b5cf6"  // Purple
        };

        // Static room tracking: DrawingId -> DrawingRoom
        private static readonly ConcurrentDictionary<string, DrawingRoom> _rooms = new();

        // Track which room each connection is in: ConnectionId -> DrawingId
        private static readonly ConcurrentDictionary<string, string> _connectionRooms = new();

        private readonly IOrgAccessService _orgAccess;
        private readonly IDrawingRepository _drawingRepository;
        private readonly ILogger<DrawingHub> _logger;

        public DrawingHub(IOrgAccessService orgAccess, IDrawingRepository drawingRepository, ILogger<DrawingHub> logger)
        {
            _orgAccess = orgAccess;
            _drawingRepository = drawingRepository;
            _logger = logger;
        }

        /// <summary>
        /// Join a drawing room. Verifies the caller is a member of the org that owns the drawing.
        /// Returns presence list on success, or throws HubException if unauthorized / full.
        /// </summary>
        public async Task<object> JoinDrawing(string drawingId, string userName, string boardType)
        {
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            var connId = Context.ConnectionId;

            RealtimeDebugger.Log("Join", $"JoinDrawing requested for drawing={drawingId}", level: RealtimeDebugger.Level.Info,
                connectionId: connId, userId: userId, drawingId: drawingId);

            if (string.IsNullOrWhiteSpace(drawingId))
            {
                RealtimeDebugger.Log("Join", "JoinDrawing rejected: empty drawingId.", level: RealtimeDebugger.Level.Warn,
                    connectionId: connId, userId: userId);
                throw new HubException("Invalid drawing id.");
            }

            // --- Membership verification ---
            // Resolve the drawing, then verify the user is a member of the drawing's org team.
            string? organizationId = null;
            bool membershipOk = false;
            try
            {
                var drawing = await _drawingRepository.GetByIdAsync(drawingId);
                if (drawing == null)
                {
                    RealtimeDebugger.Log("Join", "JoinDrawing rejected: drawing not found.", level: RealtimeDebugger.Level.Warn,
                        connectionId: connId, userId: userId, drawingId: drawingId, membershipOk: false);
                    throw new HubException("Drawing not found.");
                }
                organizationId = drawing.OrganizationId;
                membershipOk = await _orgAccess.IsCurrentUserMemberOfOrgAsync(organizationId);
            }
            catch (HubException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JoinDrawing membership check failed for drawing {DrawingId}.", drawingId);
                RealtimeDebugger.Log("Join", "JoinDrawing rejected: membership check error: " + ex.Message, level: RealtimeDebugger.Level.Error,
                    connectionId: connId, userId: userId, drawingId: drawingId, organizationId: organizationId, membershipOk: false);
                throw new HubException("Unable to verify access to this drawing.");
            }

            if (!membershipOk)
            {
                RealtimeDebugger.Log("Join", "JoinDrawing DENIED: user is not a member of the drawing's team.", level: RealtimeDebugger.Level.Warn,
                    connectionId: connId, userId: userId, drawingId: drawingId, organizationId: organizationId, membershipOk: false);
                throw new HubException("You are not a member of this team and cannot join this drawing.");
            }

            var room = _rooms.GetOrAdd(drawingId, _ => new DrawingRoom { DrawingId = drawingId, OrganizationId = organizationId });

            lock (room.Lock)
            {
                // Check capacity
                if (room.Connections.Count >= MaxUsersPerDrawing)
                {
                    RealtimeDebugger.Log("Join", $"JoinDrawing rejected: board full ({MaxUsersPerDrawing}/{MaxUsersPerDrawing}).", level: RealtimeDebugger.Level.Warn,
                        connectionId: connId, userId: userId, drawingId: drawingId, organizationId: organizationId, membershipOk: true);
                    throw new HubException($"This board is full ({MaxUsersPerDrawing}/{MaxUsersPerDrawing} users). Please try again later.");
                }

                // Assign color based on slot
                var colorIndex = room.Connections.Count % UserColors.Length;
                var color = UserColors[colorIndex];

                var connectionInfo = new ConnectionInfo
                {
                    ConnectionId = connId,
                    UserId = userId,
                    UserName = userName ?? "Anonymous",
                    Color = color,
                    BoardType = boardType ?? "whiteboard",
                    OrganizationId = organizationId,
                    DrawingId = drawingId
                };

                room.Connections.Add(connectionInfo);
                _connectionRooms[connId] = drawingId;
            }

            // Join SignalR group
            await Groups.AddToGroupAsync(connId, drawingId);

            // Build presence list
            var presence = GetPresenceList(room);
            var myColor = GetConnectionColor(room, connId);

            // Mirror room into the debugger so /api/debug/realtime can report live presence.
            RealtimeDebugger.UpsertRoom(drawingId, organizationId,
                room.Connections.Select(c => new RealtimeDebugger.PresenceEntry
                {
                    ConnectionId = c.ConnectionId,
                    UserId = c.UserId,
                    UserName = c.UserName,
                    Color = c.Color
                }));

            // Notify others in the group
            var userJoined = new
            {
                userId,
                userName = userName ?? "Anonymous",
                color = myColor,
                boardType = boardType ?? "whiteboard"
            };

            // Recipient count = others currently in the room (excluding this connection).
            int recipients = room.Connections.Count(c => c.ConnectionId != connId);

            await Clients.OthersInGroup(drawingId).SendAsync("UserJoined", userJoined);

            RealtimeDebugger.Log("Join", $"JoinDrawing OK. Joined drawing group.", level: RealtimeDebugger.Level.Info,
                connectionId: connId, userId: userId, drawingId: drawingId, organizationId: organizationId,
                membershipOk: true, recipientCount: recipients);

            return new { success = true, presence, color = myColor, organizationId };
        }

        /// <summary>
        /// Leave a drawing room explicitly (called when user navigates away).
        /// </summary>
        public async Task LeaveDrawing(string drawingId)
        {
            RealtimeDebugger.Log("Leave", "LeaveDrawing requested.", level: RealtimeDebugger.Level.Info,
                connectionId: Context.ConnectionId, userId: Context.UserIdentifier, drawingId: drawingId);
            await RemoveConnectionFromRoom(drawingId, Context.ConnectionId);
        }

        /// <summary>
        /// Relay an element change (add/update/delete) to others in the drawing.
        /// Only connections that have joined the room (and thus passed membership) may broadcast.
        /// </summary>
        public async Task SendElement(string drawingId, object element, string action)
        {
            if (!IsInRoom(drawingId, out var room))
            {
                RealtimeDebugger.Log("Broadcast", "SendElement BLOCKED: connection not in room.", level: RealtimeDebugger.Level.Warn,
                    connectionId: Context.ConnectionId, userId: Context.UserIdentifier, drawingId: drawingId, action: action);
                return;
            }

            int recipients = room.Connections.Count(c => c.ConnectionId != Context.ConnectionId);
            await Clients.OthersInGroup(drawingId).SendAsync("ElementChanged", new
            {
                element,
                action,
                userId = Context.UserIdentifier ?? Context.ConnectionId
            });

            RealtimeDebugger.Log("Broadcast", $"SendElement '{action}' relayed.", level: RealtimeDebugger.Level.Info,
                connectionId: Context.ConnectionId, userId: Context.UserIdentifier,
                drawingId: drawingId, organizationId: room.OrganizationId, action: action, recipientCount: recipients);
        }

        /// <summary>
        /// Relay cursor position to others in the drawing.
        /// </summary>
        public async Task SendCursor(string drawingId, object cursor)
        {
            if (!IsInRoom(drawingId, out var room)) return;

            var color = GetConnectionColor(room, Context.ConnectionId);
            var userName = GetConnectionUserName(room, Context.ConnectionId);

            await Clients.OthersInGroup(drawingId).SendAsync("CursorMoved", new
            {
                userId = Context.UserIdentifier ?? Context.ConnectionId,
                userName,
                color,
                cursor
            });
        }

        /// <summary>
        /// Relay board clear to others in the drawing.
        /// </summary>
        public async Task SendClear(string drawingId)
        {
            if (!IsInRoom(drawingId, out var room))
            {
                RealtimeDebugger.Log("Broadcast", "SendClear BLOCKED: connection not in room.", level: RealtimeDebugger.Level.Warn,
                    connectionId: Context.ConnectionId, userId: Context.UserIdentifier, drawingId: drawingId);
                return;
            }

            int recipients = room.Connections.Count(c => c.ConnectionId != Context.ConnectionId);
            await Clients.OthersInGroup(drawingId).SendAsync("BoardCleared", new
            {
                userId = Context.UserIdentifier ?? Context.ConnectionId
            });

            RealtimeDebugger.Log("Broadcast", "SendClear relayed.", level: RealtimeDebugger.Level.Info,
                connectionId: Context.ConnectionId, userId: Context.UserIdentifier,
                drawingId: drawingId, organizationId: room.OrganizationId, action: "clear", recipientCount: recipients);
        }

        /// <summary>
        /// Relay tool change to others in the drawing.
        /// </summary>
        public async Task SendToolChange(string drawingId, string tool)
        {
            if (!IsInRoom(drawingId, out var room)) return;

            var color = GetConnectionColor(room, Context.ConnectionId);
            var userName = GetConnectionUserName(room, Context.ConnectionId);

            await Clients.OthersInGroup(drawingId).SendAsync("ToolChanged", new
            {
                userId = Context.UserIdentifier ?? Context.ConnectionId,
                userName,
                color,
                tool
            });
        }

        /// <summary>
        /// Handle disconnection - auto-leave any room the user was in.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_connectionRooms.TryRemove(Context.ConnectionId, out var drawingId))
            {
                RealtimeDebugger.Log("Disconnect", "Connection disconnected, leaving room.", level: RealtimeDebugger.Level.Info,
                    connectionId: Context.ConnectionId, userId: Context.UserIdentifier, drawingId: drawingId);
                await RemoveConnectionFromRoom(drawingId, Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        #region Private Helpers

        /// <summary>True iff this connection has successfully joined (and thus passed membership for) the room.</summary>
        private bool IsInRoom(string drawingId, out DrawingRoom room)
        {
            room = GetRoom(drawingId)!;
            if (room == null) return false;
            lock (room.Lock)
            {
                return room.Connections.Any(c => c.ConnectionId == Context.ConnectionId);
            }
        }

        private async Task RemoveConnectionFromRoom(string drawingId, string connectionId)
        {
            var room = GetRoom(drawingId);
            if (room == null) return;

            ConnectionInfo? removed = null;
            List<ConnectionInfo> remainingPresence;

            lock (room.Lock)
            {
                removed = room.Connections.FirstOrDefault(c => c.ConnectionId == connectionId);
                if (removed != null)
                {
                    room.Connections.Remove(removed);
                }

                // Reassign colors to remaining connections
                for (int i = 0; i < room.Connections.Count; i++)
                {
                    room.Connections[i].Color = UserColors[i % UserColors.Length];
                }

                remainingPresence = room.Connections.ToList();

                // Clean up empty room
                if (room.Connections.Count == 0)
                {
                    _rooms.TryRemove(drawingId, out _);
                    RealtimeDebugger.RemoveRoom(drawingId);
                }
                else
                {
                    RealtimeDebugger.UpsertRoom(drawingId, room.OrganizationId,
                        room.Connections.Select(c => new RealtimeDebugger.PresenceEntry
                        {
                            ConnectionId = c.ConnectionId,
                            UserId = c.UserId,
                            UserName = c.UserName,
                            Color = c.Color
                        }));
                }
            }

            _connectionRooms.TryRemove(connectionId, out _);

            if (removed != null)
            {
                // Leave SignalR group
                await Groups.RemoveFromGroupAsync(connectionId, drawingId);

                // Notify others
                var presence = remainingPresence.Select(c => new
                {
                    userId = c.UserId,
                    userName = c.UserName,
                    color = c.Color
                }).ToList();

                await Clients.OthersInGroup(drawingId).SendAsync("UserLeft", new
                {
                    userId = removed.UserId
                });

                await Clients.Group(drawingId).SendAsync("PresenceUpdate", presence);

                RealtimeDebugger.Log("Leave", $"Connection removed from room. Remaining={remainingPresence.Count}.", level: RealtimeDebugger.Level.Info,
                    connectionId: connectionId, userId: removed.UserId, drawingId: drawingId, organizationId: room.OrganizationId,
                    recipientCount: remainingPresence.Count);
            }
        }

        private DrawingRoom? GetRoom(string drawingId)
        {
            _rooms.TryGetValue(drawingId, out var room);
            return room;
        }

        private List<object> GetPresenceList(DrawingRoom room)
        {
            lock (room.Lock)
            {
                return room.Connections.Select(c => (object)new
                {
                    userId = c.UserId,
                    userName = c.UserName,
                    color = c.Color
                }).ToList();
            }
        }

        private string GetConnectionColor(DrawingRoom? room, string connectionId)
        {
            if (room == null) return UserColors[0];
            lock (room.Lock)
            {
                return room.Connections.FirstOrDefault(c => c.ConnectionId == connectionId)?.Color ?? UserColors[0];
            }
        }

        private string GetConnectionUserName(DrawingRoom? room, string connectionId)
        {
            if (room == null) return "Anonymous";
            lock (room.Lock)
            {
                return room.Connections.FirstOrDefault(c => c.ConnectionId == connectionId)?.UserName ?? "Anonymous";
            }
        }

        #endregion
    }
}
