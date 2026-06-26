using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Appwrite;
using Appwrite.Services;
using DrawSync.UnitOfWork.Interface;
using System;

namespace DrawSync.Hubs
{
    public class ConnectionInfo
    {
        public string ConnectionId { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public string Color { get; set; } = null!;
        public string BoardType { get; set; } = "whiteboard";
    }

    public class DrawingRoom
    {
        public string DrawingId { get; set; } = null!;
        public List<ConnectionInfo> Connections { get; set; } = new();
        public object Lock { get; } = new();
    }

    [Authorize]
    public class DrawingHub : Hub
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Client _userClient;

        public DrawingHub(IUnitOfWork unitOfWork, Client userClient)
        {
            _unitOfWork = unitOfWork;
            _userClient = userClient;
        }

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

        /// <summary>
        /// Join a drawing room. Returns presence list on success, or throws HubException if full.
        /// </summary>
        public async Task<object> JoinDrawing(string drawingId, string userName, string boardType)
        {
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            Console.WriteLine($"[DEBUG DrawingHub] Connection {Context.ConnectionId} (User: {userId}) attempting to join drawing {drawingId}");

            // Verify drawing exists
            var drawing = await _unitOfWork.Drawings.GetByIdAsync(drawingId);
            if (drawing == null)
            {
                Console.WriteLine($"[DEBUG DrawingHub] Join failed: Drawing {drawingId} not found");
                throw new HubException("Drawing not found.");
            }

            // Verify user is in the organization owning the drawing
            try
            {
                var userTeamsService = new Teams(_userClient);
                var userTeams = await userTeamsService.List();
                if (!userTeams.Teams.Any(t => t.Id == drawing.OrganizationId))
                {
                    Console.WriteLine($"[DEBUG DrawingHub] Join failed: User {userId} is not a member of organization team {drawing.OrganizationId}");
                    throw new HubException("You are not a member of the organization owning this drawing.");
                }
            }
            catch (Exception ex) when (!(ex is HubException))
            {
                Console.WriteLine($"[DEBUG DrawingHub] Join failed: Membership check error: {ex.Message}");
                throw new HubException("Failed to verify membership: " + ex.Message);
            }

            var room = _rooms.GetOrAdd(drawingId, _ => new DrawingRoom { DrawingId = drawingId });

            lock (room.Lock)
            {
                // Check capacity
                if (room.Connections.Count >= MaxUsersPerDrawing)
                {
                    Console.WriteLine($"[DEBUG DrawingHub] Join failed: Room {drawingId} is full");
                    throw new HubException($"This board is full ({MaxUsersPerDrawing}/{MaxUsersPerDrawing} users). Please try again later.");
                }

                // Assign color based on slot
                var colorIndex = room.Connections.Count % UserColors.Length;
                var color = UserColors[colorIndex];

                var connectionInfo = new ConnectionInfo
                {
                    ConnectionId = Context.ConnectionId,
                    UserId = userId,
                    UserName = userName ?? "Anonymous",
                    Color = color,
                    BoardType = boardType ?? "whiteboard"
                };

                room.Connections.Add(connectionInfo);
                _connectionRooms[Context.ConnectionId] = drawingId;
            }

            // Join SignalR group
            await Groups.AddToGroupAsync(Context.ConnectionId, drawingId);

            // Build presence list
            var presence = GetPresenceList(room);

            // Notify others in the group
            var userJoined = new
            {
                userId = Context.UserIdentifier ?? Context.ConnectionId,
                userName = userName ?? "Anonymous",
                color = GetConnectionColor(room, Context.ConnectionId),
                boardType = boardType ?? "whiteboard"
            };

            await Clients.OthersInGroup(drawingId).SendAsync("UserJoined", userJoined);

            Console.WriteLine($"[DEBUG DrawingHub] User {userId} ({userName}) joined drawing {drawingId} successfully. Connections in room: {room.Connections.Count}");

            return new { success = true, presence, color = GetConnectionColor(room, Context.ConnectionId) };
        }

        /// <summary>
        /// Leave a drawing room explicitly (called when user navigates away).
        /// </summary>
        public async Task LeaveDrawing(string drawingId)
        {
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            Console.WriteLine($"[DEBUG DrawingHub] User {userId} explicitly leaving drawing {drawingId}");
            await RemoveConnectionFromRoom(drawingId, Context.ConnectionId);
        }

        /// <summary>
        /// Relay an element change (add/update/delete) to others in the drawing.
        /// </summary>
        public async Task SendElement(string drawingId, object element, string action)
        {
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            Console.WriteLine($"[DEBUG DrawingHub] User {userId} in drawing {drawingId} broadcasted ElementChanged (Action: {action})");
            await Clients.OthersInGroup(drawingId).SendAsync("ElementChanged", new
            {
                element,
                action,
                userId = Context.UserIdentifier ?? Context.ConnectionId
            });
        }

        /// <summary>
        /// Relay cursor position to others in the drawing.
        /// </summary>
        public async Task SendCursor(string drawingId, object cursor)
        {
            var room = GetRoom(drawingId);
            var color = GetConnectionColor(room, Context.ConnectionId);
            var userName = GetConnectionUserName(room, Context.ConnectionId);
            var userId = Context.UserIdentifier ?? Context.ConnectionId;

            Console.WriteLine($"[DEBUG DrawingHub] User {userId} in drawing {drawingId} broadcasted CursorMoved");

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
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            Console.WriteLine($"[DEBUG DrawingHub] User {userId} in drawing {drawingId} broadcasted BoardCleared");
            await Clients.OthersInGroup(drawingId).SendAsync("BoardCleared", new
            {
                userId = Context.UserIdentifier ?? Context.ConnectionId
            });
        }

        /// <summary>
        /// Relay tool change to others in the drawing.
        /// </summary>
        public async Task SendToolChange(string drawingId, string tool)
        {
            var room = GetRoom(drawingId);
            var color = GetConnectionColor(room, Context.ConnectionId);
            var userName = GetConnectionUserName(room, Context.ConnectionId);
            var userId = Context.UserIdentifier ?? Context.ConnectionId;

            Console.WriteLine($"[DEBUG DrawingHub] User {userId} in drawing {drawingId} broadcasted ToolChanged ({tool})");

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
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            Console.WriteLine($"[DEBUG DrawingHub] Connection {Context.ConnectionId} (User: {userId}) disconnected. Reason: {exception?.Message ?? "No exception"}");
            if (_connectionRooms.TryRemove(Context.ConnectionId, out var drawingId))
            {
                await RemoveConnectionFromRoom(drawingId, Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        #region Private Helpers

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
                }
            }

            _connectionRooms.TryRemove(connectionId, out _);

            if (removed != null)
            {
                Console.WriteLine($"[DEBUG DrawingHub] User {removed.UserId} removed from room {drawingId}. Remaining connections: {remainingPresence.Count}");
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
