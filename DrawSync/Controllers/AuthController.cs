using System.Security.Claims;
using DrawSync.UnitOfWork.Interface;
using DrawSync.Models;
using DrawSync.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Appwrite;
using Appwrite.Services;

namespace DrawSync.Controllers
{
    public class AuthController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Account _account;

        public AuthController(IUnitOfWork unitOfWork, Account account)
        {
            _unitOfWork = unitOfWork;
            _account = account;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Appwrite login
                var session = await _account.CreateEmailPasswordSession(model.Email, model.Password);
                
                // Get the user from Account
                var account = await _account.Get();
                Console.WriteLine($"[DEBUG] Appwrite Account ID: {account.Id}, Email: {account.Email}");
                
                // Fetch user data from 'user' table using email query instead of ID
                Console.WriteLine($"[DEBUG] Searching for user document in TablesDB with Email: {account.Email}");
                var user = await _unitOfWork.Users.GetByEmailAsync(account.Email);

                if (user == null)
                {
                    Console.WriteLine($"[DEBUG] Result from TablesDB: NULL (Document not found for Email {account.Email})");
                    // Log out from Appwrite since the DB record is missing
                    await _account.DeleteSession("current");
                    
                    TempData["LoginError"] = $"User found in Auth but missing from Database (ID: {account.Id}). Please contact support.";
                    ModelState.AddModelError(string.Empty, "User record missing from database.");
                    return View(model);
                }

                Console.WriteLine($"[DEBUG] Result from TablesDB: SUCCESS. User Name: {user.Name}");

                // Get labels (roles)
                var role = account.Labels.Contains("admin") ? "Admin" : "User";

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, role)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                return RedirectToAction("Index", "Home");
            }
            catch (AppwriteException ex)
            {
                Console.WriteLine($"Appwrite Exception during Login: {ex.Message}");
                Console.WriteLine($"Error Type: {ex.Type}, Code: {ex.Code}");
                ModelState.AddModelError(string.Empty, "Login failed: " + ex.Message);
                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General Exception during Login: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                ModelState.AddModelError(string.Empty, "An unexpected error occurred.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Create Appwrite account
                var account = await _account.Create(
                    userId: ID.Unique(),
                    email: model.Email,
                    password: model.Password,
                    name: model.Name
                );

                // Create a session for the new user so they are authenticated
                // This is required to have "users" role permission to write to the 'users' table
                await _account.CreateEmailPasswordSession(model.Email, model.Password);

                // Initialize user document in 'users' table
                var user = new User
                {
                    Id = account.Id,
                    Name = model.Name,
                    Email = model.Email
                };

                await _unitOfWork.Users.AddAsync(user);
                await _unitOfWork.SaveChangesAsync();

                // Fetch account details to get labels
                var accountDetails = await _account.Get();
                var role = accountDetails.Labels.Contains("admin") ? "Admin" : "User";

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, role)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity));

                return RedirectToAction("Index", "Home");
            }
            catch (AppwriteException ex)
            {
                ModelState.AddModelError(string.Empty, "Registration failed: " + ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            try
            {
                await _account.DeleteSession("current");
            }
            catch { /* Ignore if no session */ }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
