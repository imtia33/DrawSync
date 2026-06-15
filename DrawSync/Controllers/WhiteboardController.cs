using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DrawSync.Filters;
using System.Security.Claims;

namespace DrawSync.Controllers
{
    [Authorize]
    [VerifiedUser]
    [Route("whiteboard")]
    [Route("organization/{organizationId}/whiteboard")]
    public class WhiteboardController : Controller
    {
        private readonly DrawSync.UnitOfWork.Interface.IUnitOfWork _unitOfWork;

        public WhiteboardController(DrawSync.UnitOfWork.Interface.IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        // Local Dashboard
        [HttpGet]
        public IActionResult Index(string? organizationId)
        {
            ViewBag.OrgId = organizationId;
            return View();
        }

        // Local Drawing Board
        [HttpGet("board/{id}")]
        public async Task<IActionResult> Board(string id, string? organizationId)
        {
            ViewBag.BoardId = id;
            ViewBag.OrgId = organizationId;
            
            // Pass user identity for SignalR hub
            ViewBag.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
            ViewBag.UserName = User.Identity?.Name ?? "Anonymous";
            
            if (!string.IsNullOrEmpty(organizationId))
            {
                var drawing = await _unitOfWork.Drawings.GetByIdAsync(id);
                ViewBag.BoardType = drawing?.Type ?? "whiteboard";
                ViewBag.BoardName = drawing?.Name ?? "Untitled Board";
                ViewBag.IsOrgBoard = true;
            }
            else
            {
                // For local boards, type is determined client-side
                ViewBag.BoardType = "local";
                ViewBag.IsOrgBoard = false;
            }

            return View();
        }
    }
}
