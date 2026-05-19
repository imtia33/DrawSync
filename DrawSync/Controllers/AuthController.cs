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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DrawSync.Controllers
{
    public class AuthController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Account _account;
        private readonly Teams _teams;

        public AuthController(IUnitOfWork unitOfWork, Account account, Teams teams)
        {
            _unitOfWork = unitOfWork;
            _account = account;
            _teams = teams;
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
                // Create session using credentials
                var session = await _account.CreateEmailPasswordSession(model.Email, model.Password);
                
                // Retrieve current account status
                var account = await _account.Get();
                Console.WriteLine($"[DEBUG] Appwrite Account ID: {account.Id}, Email: {account.Email}");
                
                // Fetch corresponding database user document
                var user = await _unitOfWork.Users.GetByEmailAsync(account.Email);

                if (user == null)
                {
                    Console.WriteLine($"[DEBUG] Database User Row missing for Email: {account.Email}");
                    await _account.DeleteSession("current");
                    
                    TempData["LoginError"] = "User record missing from database. Please register again.";
                    ModelState.AddModelError(string.Empty, "User record missing from database.");
                    return View(model);
                }

                // Retrieve verification state
                var isVerified = account.EmailVerification;
                var role = account.Labels.Contains("admin") ? "Admin" : "User";

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("IsVerified", isVerified ? "true" : "false"),
                    new Claim("AppwriteSession", session.Secret)
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

                if (!isVerified)
                {
                    return RedirectToAction("VerificationPending");
                }

                // If fully verified, navigate to organization space
                var accessibleOrganizations = await _unitOfWork.Organizations.GetAllAsync();
                var firstOrg = accessibleOrganizations.FirstOrDefault();
                if (firstOrg?.Id != null)
                {
                    return RedirectToAction("Details", "Organization", new { id = firstOrg.Id });
                }

                return RedirectToAction("Index", "Organization");
            }
            catch (AppwriteException ex)
            {
                Console.WriteLine($"Appwrite Exception during Login: {ex.Message}");
                ModelState.AddModelError(string.Empty, "Login failed: " + ex.Message);
                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General Exception during Login: {ex.Message}");
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
                // Create user account on Appwrite Console
                var account = await _account.Create(
                    userId: ID.Unique(),
                    email: model.Email,
                    password: model.Password,
                    name: model.Name
                );

                // Create initial email session so we possess write permissions for tables and invite templates
                var session = await _account.CreateEmailPasswordSession(model.Email, model.Password);

                // Add User document representation to local users table
                var user = new User
                {
                    Id = account.Id,
                    Name = model.Name,
                    Email = model.Email
                };

                await _unitOfWork.Users.AddAsync(user);
                await _unitOfWork.SaveChangesAsync();

                // Create default Organization space and Team
                var teamId = ID.Unique();
                try
                {
                    await _teams.Create(teamId, model.OrganizationName);

                    var org = new Models.Organization
                    {
                        Id = teamId,
                        Name = model.OrganizationName,
                        Plan = "free"
                    };

                    await _unitOfWork.Organizations.AddAsync(org, new List<string> {
                        Permission.Read(Role.Team(teamId)),
                        Permission.Update(Role.Team(teamId)),
                        Permission.Delete(Role.Team(teamId))
                    });

                    // Track billing statistics
                    var usage = new Models.Usage
                    {
                        Id = ID.Unique(),
                        OrganizationId = teamId,
                        DrawingsCount = 0,
                        Collaborators = 1,
                        RenewDate = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd")
                    };
                    await _unitOfWork.Usage.AddAsync(usage, new List<string> {
                        Permission.Read(Role.Team(teamId)),
                        Permission.Update(Role.Team(teamId))
                    });
                }
                catch (AppwriteException ex)
                {
                    Console.WriteLine("Failed to construct organization space: " + ex.Message);
                    try
                    {
                        await _account.DeleteSession("current");
                    }
                    catch { /* Cleanup exceptions isolated */ }

                    ModelState.AddModelError(string.Empty, $"Organization creation failed: {ex.Message}");
                    return View(model);
                }

                // Automatically trigger email verification request
                try
                {
                    var callbackUrl = $"{Request.Scheme}://{Request.Host}/Auth/VerifyEmail";
                    await _account.CreateEmailVerification(callbackUrl);
                }
                catch (AppwriteException ex)
                {
                    Console.WriteLine("Failed to fire verification email: " + ex.Message);
                }

                // Sign in with 'IsVerified' = false
                var role = "User";
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("IsVerified", "false"),
                    new Claim("AppwriteSession", session.Secret)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity));

                return RedirectToAction("VerificationPending");
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
            catch { /* Ignore when session already purged */ }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ============================================
        // Email Verification System
        // ============================================

        [HttpGet]
        public async Task<IActionResult> VerificationPending()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToAction("Login");
            }

            var isVerifiedClaim = User.FindFirst("IsVerified")?.Value;
            if (isVerifiedClaim == "true")
            {
                return RedirectToAction("Index", "Home");
            }

            // Real-time synchronization check: query Appwrite to see if they completed verification in another browser window
            try
            {
                var account = await _account.Get();
                if (account.EmailVerification)
                {
                    var claims = User.Claims.ToList();
                    var oldVerified = claims.FirstOrDefault(c => c.Type == "IsVerified");
                    if (oldVerified != null) claims.Remove(oldVerified);
                    claims.Add(new Claim("IsVerified", "true"));

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                    return RedirectToAction("VerificationSuccess");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Sync Verification check failed: " + ex.Message);
            }

            ViewBag.Email = User.FindFirstValue(ClaimTypes.Email);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendVerification()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Challenge();
            }

            try
            {
                var callbackUrl = $"{Request.Scheme}://{Request.Host}/Auth/VerifyEmail";
                await _account.CreateEmailVerification(callbackUrl);
                TempData["ResendSuccess"] = "Verification email sent successfully! Please check your inbox.";
            }
            catch (AppwriteException ex)
            {
                TempData["ResendError"] = "Failed to resend verification email: " + ex.Message;
            }
            catch (Exception)
            {
                TempData["ResendError"] = "An unexpected error occurred.";
            }

            return RedirectToAction("VerificationPending");
        }

        [HttpGet]
        public async Task<IActionResult> VerifyEmail(string userId, string secret)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(secret))
            {
                TempData["VerificationError"] = "Invalid or expired verification link.";
                return RedirectToAction("Login");
            }

            try
            {
                // Request Appwrite to mark email verified
                await _account.UpdateEmailVerification(userId, secret);
                
                // Update claims if current user is logged in
                if (User.Identity?.IsAuthenticated == true)
                {
                    var claims = User.Claims.ToList();
                    var oldVerified = claims.FirstOrDefault(c => c.Type == "IsVerified");
                    if (oldVerified != null) claims.Remove(oldVerified);
                    claims.Add(new Claim("IsVerified", "true"));

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                    return RedirectToAction("VerificationSuccess");
                }

                TempData["LoginSuccess"] = "Email verified successfully! You can now log in.";
                return RedirectToAction("Login");
            }
            catch (AppwriteException ex)
            {
                Console.WriteLine("Appwrite verification error callback: " + ex.Message);
                TempData["VerificationError"] = "Verification failed: " + ex.Message;
                return RedirectToAction("Login");
            }
        }

        [HttpGet]
        public IActionResult VerificationSuccess()
        {
            return View();
        }

        // ============================================
        // Google OAuth2 System
        // ============================================

        [HttpGet]
        public async Task<IActionResult> ContinueWithGoogle()
        {
            try
            {
                var successUrl = $"{Request.Scheme}://{Request.Host}/Auth/GoogleCallback";
                var failureUrl = $"{Request.Scheme}://{Request.Host}/Auth/Login";
                
                var authUrl = await _account.CreateOAuth2Token(Appwrite.Enums.OAuthProvider.Google, successUrl, failureUrl);
                return Redirect(authUrl);
            }
            catch (AppwriteException ex)
            {
                TempData["LoginError"] = "Failed to start Google sign-in: " + ex.Message;
                return RedirectToAction("Login");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GoogleCallback(string userId, string secret)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(secret))
            {
                TempData["LoginError"] = "Google callback parameters are missing.";
                return RedirectToAction("Login");
            }

            try
            {
                // Exchange credentials for session token
                var session = await _account.CreateSession(userId, secret);
                
                // Inject session into Client service manually for this execution thread
                var scopedClient = HttpContext.RequestServices.GetRequiredService<Client>();
                scopedClient.SetSession(session.Secret);

                var account = await _account.Get();
                
                // Check database representation
                var user = await _unitOfWork.Users.GetByEmailAsync(account.Email);
                
                if (user == null)
                {
                    // Redirect to final onboarding stage for completing new account registrations
                    TempData["GoogleUserId"] = account.Id;
                    TempData["GoogleName"] = account.Name;
                    TempData["GoogleEmail"] = account.Email;
                    TempData["GoogleSessionSecret"] = session.Secret;
                    
                    return RedirectToAction("CompleteGoogleSignup");
                }

                // Existing account logs in directly
                var role = account.Labels.Contains("admin") ? "Admin" : "User";
                
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("IsVerified", "true"), // Google emails verified by default
                    new Claim("AppwriteSession", session.Secret)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                var accessibleOrganizations = await _unitOfWork.Organizations.GetAllAsync();
                var firstOrg = accessibleOrganizations.FirstOrDefault();
                if (firstOrg?.Id != null)
                {
                    return RedirectToAction("Details", "Organization", new { id = firstOrg.Id });
                }

                return RedirectToAction("Index", "Organization");
            }
            catch (AppwriteException ex)
            {
                Console.WriteLine("Google Login callback exception: " + ex.Message);
                TempData["LoginError"] = "Google login failed: " + ex.Message;
                return RedirectToAction("Login");
            }
        }

        [HttpGet]
        public IActionResult CompleteGoogleSignup()
        {
            var email = TempData["GoogleEmail"]?.ToString() ?? TempData.Peek("GoogleEmail")?.ToString();
            var name = TempData["GoogleName"]?.ToString() ?? TempData.Peek("GoogleName")?.ToString();
            
            if (string.IsNullOrEmpty(email))
            {
                TempData["LoginError"] = "Google session expired. Please sign in again.";
                return RedirectToAction("Login");
            }

            var model = new CompleteGoogleSignupViewModel
            {
                Email = email,
                Name = name
            };

            TempData.Keep("GoogleUserId");
            TempData.Keep("GoogleName");
            TempData.Keep("GoogleEmail");
            TempData.Keep("GoogleSessionSecret");

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteGoogleSignup(CompleteGoogleSignupViewModel model)
        {
            var email = TempData["GoogleEmail"]?.ToString();
            var name = TempData["GoogleName"]?.ToString();
            var userId = TempData["GoogleUserId"]?.ToString();
            var sessionSecret = TempData["GoogleSessionSecret"]?.ToString();

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionSecret))
            {
                TempData["LoginError"] = "Google session expired. Please sign in again.";
                return RedirectToAction("Login");
            }

            if (!ModelState.IsValid)
            {
                model.Email = email;
                model.Name = name;
                TempData.Keep("GoogleUserId");
                TempData.Keep("GoogleName");
                TempData.Keep("GoogleEmail");
                TempData.Keep("GoogleSessionSecret");
                return View(model);
            }

            try
            {
                // Create database representation
                var user = new User
                {
                    Id = userId,
                    Name = name ?? "Google User",
                    Email = email
                };

                await _unitOfWork.Users.AddAsync(user);
                await _unitOfWork.SaveChangesAsync();

                // Setup Organization and Teams
                var teamId = ID.Unique();
                await _teams.Create(teamId, model.OrganizationName);

                var org = new Models.Organization
                {
                    Id = teamId,
                    Name = model.OrganizationName,
                    Plan = "free"
                };

                await _unitOfWork.Organizations.AddAsync(org, new List<string> {
                    Permission.Read(Role.Team(teamId)),
                    Permission.Update(Role.Team(teamId)),
                    Permission.Delete(Role.Team(teamId))
                });

                var usage = new Models.Usage
                {
                    Id = ID.Unique(),
                    OrganizationId = teamId,
                    DrawingsCount = 0,
                    Collaborators = 1,
                    RenewDate = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd")
                };
                await _unitOfWork.Usage.AddAsync(usage, new List<string> {
                    Permission.Read(Role.Team(teamId)),
                    Permission.Update(Role.Team(teamId))
                });

                await _unitOfWork.SaveChangesAsync();

                // SignIn to Cookie Authorization
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, "User"),
                    new Claim("IsVerified", "true"),
                    new Claim("AppwriteSession", sessionSecret)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                return RedirectToAction("Details", "Organization", new { id = teamId });
            }
            catch (AppwriteException ex)
            {
                Console.WriteLine("Complete Google Signup Error: " + ex.Message);
                ModelState.AddModelError(string.Empty, "Failed to complete signup: " + ex.Message);
                
                TempData.Keep("GoogleUserId");
                TempData.Keep("GoogleName");
                TempData.Keep("GoogleEmail");
                TempData.Keep("GoogleSessionSecret");
                
                model.Email = email;
                model.Name = name;
                return View(model);
            }
        }

        // ============================================
        // Password Reset System (Preferred)
        // ============================================

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var callbackUrl = $"{Request.Scheme}://{Request.Host}/Auth/ResetPassword";
                await _account.CreateRecovery(model.Email, callbackUrl);
                ViewBag.SuccessMessage = "Recovery email has been dispatched. Please check your inbox.";
            }
            catch (AppwriteException ex)
            {
                ModelState.AddModelError(string.Empty, "Failed to request recovery: " + ex.Message);
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult ResetPassword(string userId, string secret)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(secret))
            {
                TempData["LoginError"] = "Invalid or expired reset token.";
                return RedirectToAction("Login");
            }

            ViewBag.UserId = userId;
            ViewBag.Secret = secret;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, string userId, string secret)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(secret))
            {
                ModelState.AddModelError(string.Empty, "Token parameters are missing. Request recovery again.");
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                ViewBag.UserId = userId;
                ViewBag.Secret = secret;
                return View(model);
            }

            try
            {
                await _account.UpdateRecovery(userId, secret, model.Password);
                TempData["LoginSuccess"] = "Password has been updated. Please log in.";
                return RedirectToAction("Login");
            }
            catch (AppwriteException ex)
            {
                ModelState.AddModelError(string.Empty, "Password update failed: " + ex.Message);
                ViewBag.UserId = userId;
                ViewBag.Secret = secret;
                return View(model);
            }
        }
    }
}
