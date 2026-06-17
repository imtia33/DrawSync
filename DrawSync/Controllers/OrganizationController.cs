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

        public OrganizationController(IUnitOfWork unitOfWork, Account account)
        {
            _unitOfWork = unitOfWork;
            _account = account;
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
