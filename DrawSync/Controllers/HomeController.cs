using DrawSync.Models;
using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;

namespace DrawSync.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUnitOfWork _unitOfWork;

        public HomeController(ILogger<HomeController> logger, IUnitOfWork unitOfWork)
        {
            _logger = logger;
            _unitOfWork = unitOfWork;
        }

        public async Task<IActionResult> Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var email = User.FindFirstValue(ClaimTypes.Email);
                if (!string.IsNullOrEmpty(email))
                {
                    try
                    {
                        var userRow = await _unitOfWork.Users.GetByEmailAsync(email);
                        ViewBag.UserRowData = userRow;
                        ViewBag.RawApiResponse = userRow != null ? Newtonsoft.Json.JsonConvert.SerializeObject(userRow, Newtonsoft.Json.Formatting.Indented) : $"User row returned null from Repository for email: {email}";
                    }
                    catch (Exception ex)
                    {
                        ViewBag.RawApiResponse = $"Error fetching from Appwrite: {ex.Message}";
                    }
                }
            }
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [Authorize(Roles = "Admin")]
        public IActionResult AdminOnly()
        {
            return Content("Only admins can see this!");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
