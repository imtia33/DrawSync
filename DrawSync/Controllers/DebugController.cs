using DrawSync.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DrawSync.Controllers
{
    /// <summary>
    /// Development/diagnostics endpoint for inspecting realtime broadcast behavior.
    /// Exposes recent DrawingHub events (joins, leaves, broadcasts, membership decisions) and the
    /// current room presence, so we can verify that broadcasts are correctly restricted to team
    /// members present in a drawing.
    ///
    /// Available to any authenticated user (read-only diagnostics).
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/debug")]
    public class DebugController : ControllerBase
    {
        [HttpGet("realtime")]
        public ActionResult GetRealtime([FromQuery] int count = 100)
        {
            return Ok(new
            {
                totalEvents = RealtimeDebugger.TotalEvents,
                events = RealtimeDebugger.GetRecentEvents(count),
                rooms = RealtimeDebugger.GetRooms(),
                serverTimeUtc = DateTime.UtcNow
            });
        }
    }
}
