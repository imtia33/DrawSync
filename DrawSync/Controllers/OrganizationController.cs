using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DrawSync.Controllers
{
    [Authorize]
    public class OrganizationController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;

        public OrganizationController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null) return RedirectToAction("Index", "Home");

            var orgs = new List<Models.Organization>();
            foreach (var orgId in user.Organizations)
            {
                var org = await _unitOfWork.Organizations.GetByIdAsync(orgId);
                if (org != null) orgs.Add(org);
            }

            return View(orgs);
        }

        public async Task<IActionResult> Details(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null) return RedirectToAction("Index", "Home");

            if (!user.Organizations.Contains(id))
            {
                return Forbid();
            }

            var org = await _unitOfWork.Organizations.GetByIdAsync(id);
            if (org == null) return NotFound();

            return View("Dashboard", org);
        }
    }
}
