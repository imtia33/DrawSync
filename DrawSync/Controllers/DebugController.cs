using DrawSync.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DrawSync.Controllers
{
    /// <summary>
    /// Development-only diagnostics endpoint for inspecting realtime broadcast behavior.
    /// Exposes recent DrawingHub events (joins, leaves, broadcasts, membership decisions) and the
    /// current room presence, so we can verify that broadcasts are correctly restricted to team
    /// members present in a drawing.
    ///
    /// Only available when the host is running in the Development environment. In production this
    /// controller returns 404 for every action, so no internal telemetry is exposed.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/debug")]
    public class DebugController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public DebugController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpGet("realtime")]
        public ActionResult GetRealtime([FromQuery] int count = 100)
        {
            if (!_env.IsDevelopment())
            {
                return NotFound();
            }

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
