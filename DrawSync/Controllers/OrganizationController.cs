using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using DrawSync.Filters;
using Appwrite;
using Appwrite.Services;

namespace DrawSync.Controllers
{
    [Authorize]
    [VerifiedUser]
    public class OrganizationController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Account _account;
        private readonly Client _userClient;

        public OrganizationController(IUnitOfWork unitOfWork, Account account, Client userClient)
        {
            _unitOfWork = unitOfWork;
            _account = account;
            _userClient = userClient;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            try
            {
                var userTeamsService = new Teams(_userClient);
                var userTeams = await userTeamsService.List();
                var teamIds = userTeams.Teams.Select(t => t.Id).ToList();

                var orgs = await _unitOfWork.Organizations.GetByIdsAsync(teamIds);
                return View(orgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to fetch user organizations: {ex.Message}");
                return View(Enumerable.Empty<DrawSync.Models.Organization>());
            }
        }

        public async Task<IActionResult> Details(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            try
            {
                var userTeamsService = new Teams(_userClient);
                var userTeams = await userTeamsService.List();
                if (!userTeams.Teams.Any(t => t.Id == id))
                {
                    return Forbid();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to verify team membership: {ex.Message}");
                return Forbid();
            }

            var org = await _unitOfWork.Organizations.GetByIdAsync(id);
            if (org == null) return NotFound();

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
