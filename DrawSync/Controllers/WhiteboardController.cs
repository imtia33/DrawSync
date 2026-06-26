using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DrawSync.Filters;
using DrawSync.Services;
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
        private readonly IOrgAccessService _orgAccess;

        public WhiteboardController(DrawSync.UnitOfWork.Interface.IUnitOfWork unitOfWork, IOrgAccessService orgAccess)
        {
            _unitOfWork = unitOfWork;
            _orgAccess = orgAccess;
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
                // Page-level access control: only team members may open an org board.
                if (!await _orgAccess.IsCurrentUserMemberOfOrgAsync(organizationId))
                {
                    return RedirectToAction("AccessDenied", "Auth");
                }

                var drawing = await _unitOfWork.Drawings.GetByIdAsync(id);
                // Defense in depth: the drawing must belong to this org.
                if (drawing == null || drawing.OrganizationId != organizationId)
                {
                    return NotFound();
                }
                ViewBag.BoardType = drawing.Type ?? "whiteboard";
                ViewBag.BoardName = drawing.Name ?? "Untitled Board";
                ViewBag.IsOrgBoard = true;
                ViewBag.IsOrgAdmin = await _orgAccess.IsCurrentUserOrgAdminAsync(organizationId);
            }
            else
            {
                // For local boards, type is determined client-side
                ViewBag.BoardType = "local";
                ViewBag.IsOrgBoard = false;
                ViewBag.IsOrgAdmin = false;
            }

            return View();
        }
    }
}
