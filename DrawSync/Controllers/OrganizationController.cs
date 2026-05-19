using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using DrawSync.Filters;

namespace DrawSync.Controllers
{
    [Authorize]
    [VerifiedUser]
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

            var orgs = await _unitOfWork.Organizations.GetAllAsync();
            return View(orgs);
        }

        public async Task<IActionResult> Details(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            var org = await _unitOfWork.Organizations.GetByIdAsync(id);
            if (org == null) return NotFound();

            return View("Dashboard", org);
        }
    }
}
