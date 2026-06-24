using DrawSync.Services;
using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using DrawSync.Filters;
using Appwrite.Services;

namespace DrawSync.Controllers
{
    [Authorize]
    [VerifiedUser]
    public class OrganizationController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Account _account;
        private readonly IOrgAccessService _orgAccess;

        public OrganizationController(IUnitOfWork unitOfWork, Account account, IOrgAccessService orgAccess)
        {
            _unitOfWork = unitOfWork;
            _account = account;
            _orgAccess = orgAccess;
        }

        /// <summary>
        /// Lists ONLY the organizations (teams) the current user is a member of.
        /// Uses a session-scoped Appwrite Teams.List() so the server never relies on the API key
        /// to enumerate every org on the platform.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            var orgs = await _orgAccess.GetCurrentUserOrganizationsAsync();
            return View(orgs);
        }

        /// <summary>
        /// Opens an organization dashboard. Membership is verified through the session-scoped
        /// Teams list — non-members are redirected to AccessDenied instead of seeing the org.
        /// </summary>
        public async Task<IActionResult> Details(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            if (string.IsNullOrEmpty(id)) return NotFound();

            // Membership check: only members of the org's team can open it.
            if (!await _orgAccess.IsCurrentUserMemberOfOrgAsync(id))
            {
                return RedirectToAction("AccessDenied", "Auth");
            }

            var org = await _unitOfWork.Organizations.GetByIdAsync(id);
            if (org == null) return NotFound();

            // Surface the current user's admin status so the dashboard can show/hide admin actions.
            ViewBag.IsOrgAdmin = await _orgAccess.IsCurrentUserOrgAdminAsync(id);

            try
            {
                var jwtObj = await _account.CreateJWT();
                ViewBag.AppwriteJwt = jwtObj.Jwt;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to generate Appwrite JWT: {ex.Message}");
            }

            return View("Dashboard", org);
        }
    }
}
