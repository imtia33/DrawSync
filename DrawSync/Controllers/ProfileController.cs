using System.Security.Claims;
using DrawSync.Models;
using DrawSync.Models.ViewModels;
using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Appwrite;
using Appwrite.Services;

namespace DrawSync.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Account _account;

        public ProfileController(IUnitOfWork unitOfWork, Account account)
        {
            _unitOfWork = unitOfWork;
            _account = account;
        }

        public async Task<IActionResult> Index()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");

            var user = await _unitOfWork.Users.GetProfileAsync(userIdStr);

            if (user == null)
            {
                return NotFound("Profile not found.");
            }

            var account = await _account.Get();
            var role = account.Labels.Contains("admin") ? "Admin" : "User";

            var model = new ProfileViewModel
            {
                Name = user.Name,
                Email = user.Email,
                RoleName = role,
                CreatedAt = DateTime.TryParse(user.CreatedAt, out var dt) ? dt : DateTime.MinValue
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");

            var user = await _unitOfWork.Users.GetByIdAsync(userIdStr);

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

            var user = await _unitOfWork.Users.GetByIdAsync(userIdStr);

            if (user == null) return NotFound();

            // Update Name in Appwrite Table
            user.Name = model.Name;

            // Password Update Logic (via Account)
            if (!string.IsNullOrEmpty(model.NewPassword))
            {
                if (string.IsNullOrEmpty(model.CurrentPassword))
                {
                    ModelState.AddModelError("CurrentPassword", "Current password is required to set a new one.");
                    return View(model);
                }

                try
                {
                    await _account.UpdatePassword(model.NewPassword, model.CurrentPassword);
                }
                catch (AppwriteException ex)
                {
                    ModelState.AddModelError("CurrentPassword", "Password update failed: " + ex.Message);
                    return View(model);
                }
            }

            // Update Name in Account as well
            if (user.Name != model.Name)
            {
                try
                {
                    await _account.UpdateName(model.Name);
                }
                catch (AppwriteException ex)
                {
                    ModelState.AddModelError("Name", "Name update failed: " + ex.Message);
                    return View(model);
                }
            }

            await _unitOfWork.Users.UpdateAsync(userIdStr, user);
            await _unitOfWork.SaveChangesAsync();

            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
