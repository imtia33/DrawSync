using System.Security.Claims;
using DrawSync.Models;
using DrawSync.Models.ViewModels;
using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DrawSync.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly PasswordHasher<User> _passwordHasher;

        public ProfileController(IUnitOfWork unitOfWork, PasswordHasher<User> passwordHasher)
        {
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
        }

        public async Task<IActionResult> Index()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");

            var userId = int.Parse(userIdStr);
            var user = await _unitOfWork.Users.GetProfileAsync(userId);

            if (user == null)
            {
                // If RLS is working, trying to access another ID would return null.
                // But here we are using the ID from the claim.
                return NotFound("Profile not found or access denied by RLS.");
            }

            var model = new ProfileViewModel
            {
                Name = user.Name,
                Email = user.Email,
                RoleName = user.Role.Name,
                CreatedAt = user.CreatedAt
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");

            var userId = int.Parse(userIdStr);
            var user = await _unitOfWork.Users.GetByIdAsync(userId);

            if (user == null) return NotFound();

            var model = new EditProfileViewModel
            {
                Name = user.Name
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditProfileViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");

            var userId = int.Parse(userIdStr);
            var user = await _unitOfWork.Users.GetByIdAsync(userId);

            if (user == null) return NotFound();

            // Update Name
            user.Name = model.Name;

            // Password Update Logic
            if (!string.IsNullOrEmpty(model.NewPassword))
            {
                if (string.IsNullOrEmpty(model.CurrentPassword))
                {
                    ModelState.AddModelError("CurrentPassword", "Current password is required to set a new one.");
                    return View(model);
                }

                var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.CurrentPassword);
                if (verificationResult == PasswordVerificationResult.Failed)
                {
                    ModelState.AddModelError("CurrentPassword", "The current password you entered is incorrect.");
                    return View(model);
                }

                user.PasswordHash = _passwordHasher.HashPassword(user, model.NewPassword);
            }

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
