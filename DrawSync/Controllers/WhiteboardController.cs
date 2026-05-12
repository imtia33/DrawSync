using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DrawSync.Controllers
{
    [Authorize]
    [Route("whiteboard")]
    public class WhiteboardController : Controller
    {
        // Local Dashboard
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        // Local Drawing Board
        [HttpGet("board/{id}")]
        public IActionResult Board(string id)
        {
            ViewBag.BoardId = id;
            return View();
        }
    }
}
