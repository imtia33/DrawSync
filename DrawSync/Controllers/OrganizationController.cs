using DrawSync.Services;
using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using DrawSync.Filters;
using Appwrite;
using Appwrite.Services;
using DrawSync.Models;
using Organization = DrawSync.Models.Organization;

namespace DrawSync.Controllers
{
    [Authorize]
    [VerifiedUser]
    public class OrganizationController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Account _account;
        private readonly IOrgAccessService _orgAccess;
        private readonly Client _appwriteClient;

        public OrganizationController(IUnitOfWork unitOfWork, Account account, IOrgAccessService orgAccess, Client appwriteClient)
        {
            _unitOfWork = unitOfWork;
            _account = account;
            _orgAccess = orgAccess;
            _appwriteClient = appwriteClient;
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

        [HttpPost]
        public async Task<IActionResult> Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return RedirectToAction("Index");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            try
            {
                var teams = new Teams(_appwriteClient);
                // Create Team in Appwrite. Pass the owner role explicitly.
                var team = await teams.Create(
                    teamId: ID.Unique(),
                    name: name,
                    roles: new List<string> { "owner" }
                );

                // Create Organization in our Appwrite database
                var newOrg = new Organization
                {
                    Id = team.Id,
                    Name = name,
                    Plan = "free"
                };

                // The creator must be granted access in Appwrite row permissions.
                // We add read/update/delete for the newly created team.
                var permissions = new List<string> 
                { 
                    Permission.Read(Role.Team(team.Id)),
                    Permission.Update(Role.Team(team.Id)),
                    Permission.Delete(Role.Team(team.Id))
                };
                
                await _unitOfWork.Organizations.AddAsync(newOrg, permissions);
                
                return RedirectToAction("Details", new { id = team.Id });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to create organization: {ex.Message}");
                TempData["Error"] = "Failed to create organization.";
                return RedirectToAction("Index");
            }
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
