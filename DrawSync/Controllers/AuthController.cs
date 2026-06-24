using System.Security.Claims;
using DrawSync.UnitOfWork.Interface;
using DrawSync.Models;
using DrawSync.Models.ViewModels;
using DrawSync.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Appwrite;
using Appwrite.Services;
using DrawSync.Helpers;

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
        private readonly IConfiguration _config;
        private readonly IOrgAccessService _orgAccess;
        private readonly Client _client;

        public AuthController(IUnitOfWork unitOfWork, Account account, Teams teams, IConfiguration config, IOrgAccessService orgAccess, Client client)
        {
            _unitOfWork = unitOfWork;
            _account = account;
            _teams = teams;
            _config = config;
            _orgAccess = orgAccess;
            _client = client;
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
                var endpoint = _config["Appwrite:Endpoint"]!;
                var project = _config["Appwrite:Project"]!;

                // CRITICAL: In Appwrite 1.6+, Session.Secret is empty — the secret is
                // delivered only via the Set-Cookie header (a_session_{projectId}). The
                // SDK doesn't surface response headers, so we perform a thin raw REST
                // login that lets us capture that cookie. Client.SetSession(cookieValue)
                // is what actually authenticates subsequent session-scoped SDK calls.
                var sessionCookie = await SessionCookieHelper.CreateEmailPasswordSessionAsync(
                    endpoint, project, model.Email, model.Password);

                if (string.IsNullOrEmpty(sessionCookie))
                {
                    Console.WriteLine("[Login] Failed to capture session cookie from Set-Cookie header.");
                    ModelState.AddModelError(string.Empty, "Login failed: could not establish session.");
                    return View(model);
                }

                // Also create the session via the SDK so the scoped client is authenticated
                // for the _account.Get() call below. (The SDK applies the cookie internally.)
                Appwrite.Models.Session session;
                try
                {
                    session = await _account.CreateEmailPasswordSession(model.Email, model.Password);
                }
                catch (AppwriteException ex)
                {
                    Console.WriteLine($"[Login] SDK CreateEmailPasswordSession failed: {ex.Message}");
                    ModelState.AddModelError(string.Empty, "Login failed: " + ex.Message);
                    return View(model);
                }

                // Bind the captured cookie to the scoped client so _account.Get() runs as
                // the user (SetSession expects the full cookie value, NOT the bare secret).
                _client.SetSession(sessionCookie);

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
                    // Store the full cookie value (not the empty session.Secret) so every
                    // per-request scoped Client can re-authenticate via SetSession(cookie).
                    new Claim("AppwriteSession", sessionCookie)
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

                // If fully verified, navigate to organization space.
                // Use the just-created session cookie to list ONLY the organizations this
                // user belongs to (cross-checked against Teams.List — see OrgAccessService).
                Console.WriteLine($"[Login] sessionCookie len={sessionCookie.Length}, isVerified={isVerified}");
                var accessibleOrganizations = await _orgAccess.GetOrganizationsForSessionAsync(sessionCookie);
                Console.WriteLine($"[Login] GetOrganizationsForSessionAsync returned {accessibleOrganizations.Count} org(s)");
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

                // Create initial email session so we possess write permissions for tables and invite templates.
                // CRITICAL: Session.Secret is empty in Appwrite 1.6+ — capture the cookie from Set-Cookie.
                var endpoint = _config["Appwrite:Endpoint"]!;
                var project = _config["Appwrite:Project"]!;
                var sessionCookie = await SessionCookieHelper.CreateEmailPasswordSessionAsync(
                    endpoint, project, model.Email, model.Password);
                if (string.IsNullOrEmpty(sessionCookie))
                {
                    ModelState.AddModelError(string.Empty, "Registration failed: could not establish session.");
                    return View(model);
                }

                var session = await _account.CreateEmailPasswordSession(model.Email, model.Password);

                // Bind the captured COOKIE (not the empty session.Secret) to the scoped client
                // so subsequent session-scoped calls (e.g. CreateEmailVerification) act as the user.
                _client.SetSession(sessionCookie);

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
                    Console.WriteLine($"[Register] Creating team teamId={teamId} name={model.OrganizationName} for user={account.Id}");
                    await _teams.Create(teamId, model.OrganizationName);
                    Console.WriteLine("[Register] Team created OK.");

                    // CRITICAL: _teams uses the server API key, so the team's creator/owner is the
                    // application — NOT the just-registered user. Without explicitly granting the
                    // user a membership, they would not be able to read their own organization row
                    // (which is scoped to Role.Team(teamId)). Add the user as a team "owner".
                    try
                    {
                        await _teams.CreateMembership(
                            teamId: teamId,
                            roles: new List<string> { "owner" },
                            email: model.Email,
                            userId: account.Id,
                            name: model.Name
                        );
                        Console.WriteLine("[Register] Team membership (owner) added OK.");
                    }
                    catch (AppwriteException mex)
                    {
                        Console.WriteLine("[Register] Failed to add user as team owner: " + mex.Message);
                    }

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
                    new Claim("AppwriteSession", sessionCookie)
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
        public IActionResult ContinueWithGoogle()
        {
            try
            {
                // Force HTTPS scheme to prevent browsers from stripping query parameters 
                // during a secure (HTTPS Appwrite) to insecure (HTTP Local) downgrade
                var successUrl = $"https://{Request.Host}/Auth/GoogleCallback";
                var failureUrl = $"https://{Request.Host}/Auth/Login";
                
                var endpoint = _config["Appwrite:Endpoint"]?.TrimEnd('/');
                var project = _config["Appwrite:Project"];
                
                // CRITICAL: Must use /account/tokens/oauth2/... for server-side callback parameter mapping
                var authUrl = $"{endpoint}/account/tokens/oauth2/google" +
                              $"?project={project}" +
                              $"&success={Uri.EscapeDataString(successUrl)}" +
                              $"&failure={Uri.EscapeDataString(failureUrl)}";

                Console.WriteLine($"[DEBUG] Manually Constructed Google Auth URL: {authUrl}");
                return Redirect(authUrl);
            }
            catch (Exception ex)
            {
                TempData["LoginError"] = "Failed to start Google sign-in: " + ex.Message;
                return RedirectToAction("Login");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GoogleCallback(string userId, string secret)
        {
            var queryParams = HttpContext.Request.Query.Select(q => $"{q.Key} = {q.Value}").ToList();
            var debugInfo = $"[DEBUG] GoogleCallback incoming params: {string.Join(", ", queryParams)}";
            Console.WriteLine(debugInfo);

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(secret))
            {
                var fullUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
                return Content($@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <title>OAuth Diagnostic Screen</title>
                        <style>
                            body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; padding: 40px; background: #121214; color: #e1e1e6; line-height: 1.6; }}
                            .card {{ background: #202024; border: 1px solid #323238; padding: 30px; border-radius: 8px; max-width: 800px; margin: 0 auto; box-shadow: 0 4px 12px rgba(0,0,0,0.5); }}
                            h2 {{ color: #FD366E; margin-top: 0; }}
                            code {{ background: #121214; padding: 6px 10px; border-radius: 4px; color: #ff5e5e; font-family: monospace; display: block; word-break: break-all; margin-top: 8px; }}
                            ul {{ padding-left: 20px; }}
                            li {{ margin-bottom: 8px; font-family: monospace; color: #e1e1e6; }}
                            .btn {{ display: inline-block; background: #FD366E; color: white; padding: 10px 20px; text-decoration: none; border-radius: 4px; font-weight: bold; margin-top: 20px; }}
                            .btn:hover {{ background: #e02f60; }}
                        </style>
                    </head>
                    <body>
                        <div class='card'>
                            <h2>[Diagnostic] Google Callback Handler</h2>
                            <p>You have successfully landed on the callback route, but the required Appwrite OAuth credentials (<code>userId</code> and <code>secret</code>) were not found.</p>
                            <hr style='border: 0; border-top: 1px solid #323238; margin: 20px 0;' />
                            <p><strong>Full Browser Request URL:</strong></p>
                            <code>{fullUrl}</code>
                            <p><strong>Detected Query Parameters ({queryParams.Count}):</strong></p>
                            <ul>
                                {(queryParams.Count == 0 ? "<li>(No query parameters detected in the request)</li>" : string.Join("", queryParams.Select(p => $"<li>{p}</li>")))}
                            </ul>
                            <p style='color: #888; font-size: 0.9rem; margin-top: 20px;'>This screen is paused and will not auto-redirect so you can inspect the browser URL bar.</p>
                            <a href='/Auth/Login' class='btn'>Back to Login</a>
                        </div>
                    </body>
                    </html>
                ", "text/html");
            }

            try
            {
                var endpoint = _config["Appwrite:Endpoint"]!;
                var project = _config["Appwrite:Project"]!;

                // CRITICAL: In Appwrite 1.6+ Session.Secret is empty; the OAuth2 callback
                // gives us (userId, secret) which we must exchange for a session. Capture
                // the a_session_{projectId} cookie from the Set-Cookie header — that's what
                // Client.SetSession() needs to authenticate subsequent session-scoped calls.
                var sessionCookie = await SessionCookieHelper.CreateSessionFromOAuthAsync(
                    endpoint, project, userId, secret);

                if (string.IsNullOrEmpty(sessionCookie))
                {
                    Console.WriteLine("[GoogleCallback] Failed to capture session cookie from Set-Cookie header.");
                    TempData["LoginError"] = "Google login failed: could not establish session.";
                    return RedirectToAction("Login");
                }

                // Also call the SDK so the scoped client is authenticated for _account.Get().
                var session = await _account.CreateSession(userId, secret);

                // Bind the captured COOKIE (not the bare OAuth secret) to the scoped client.
                var scopedClient = HttpContext.RequestServices.GetRequiredService<Client>();
                scopedClient.SetSession(sessionCookie);

                var account = await _account.Get();

                // Check database representation
                var user = await _unitOfWork.Users.GetByEmailAsync(account.Email);

                if (user == null)
                {
                    // Redirect to final onboarding stage for completing new account registrations via query parameters
                    return RedirectToAction("CompleteGoogleSignup", new {
                        email = account.Email,
                        name = account.Name,
                        userId = account.Id,
                        sessionSecret = sessionCookie
                    });
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
                    new Claim("AppwriteSession", sessionCookie)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                var accessibleOrganizations = await _orgAccess.GetOrganizationsForSessionAsync(sessionCookie);
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
        public IActionResult CompleteGoogleSignup(string? email, string? name, string? userId, string? sessionSecret)
        {
            // Read from query string parameters first, fallback to TempData
            email ??= TempData["GoogleEmail"]?.ToString() ?? TempData.Peek("GoogleEmail")?.ToString();
            name ??= TempData["GoogleName"]?.ToString() ?? TempData.Peek("GoogleName")?.ToString();
            userId ??= TempData["GoogleUserId"]?.ToString() ?? TempData.Peek("GoogleUserId")?.ToString();
            sessionSecret ??= TempData["GoogleSessionSecret"]?.ToString() ?? TempData.Peek("GoogleSessionSecret")?.ToString();
            
            if (string.IsNullOrEmpty(email))
            {
                TempData["LoginError"] = "Google session expired. Please sign in again.";
                return RedirectToAction("Login");
            }

            var model = new CompleteGoogleSignupViewModel
            {
                Email = email,
                Name = name,
                UserId = userId,
                SessionSecret = sessionSecret
            };

            // Set TempData for fallback compatibility
            TempData["GoogleUserId"] = userId;
            TempData["GoogleName"] = name;
            TempData["GoogleEmail"] = email;
            TempData["GoogleSessionSecret"] = sessionSecret;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteGoogleSignup(CompleteGoogleSignupViewModel model)
        {
            // Log form fields to server console and capture for display
            var formFields = HttpContext.Request.Form.Select(f => $"{f.Key} = {f.Value}").ToList();
            var debugInfo = $"[DEBUG] POST CompleteGoogleSignup incoming form: {string.Join(", ", formFields)}";
            Console.WriteLine(debugInfo);

            // Read from posted model values first, fallback to TempData
            var email = model.Email ?? TempData["GoogleEmail"]?.ToString();
            var name = model.Name ?? TempData["GoogleName"]?.ToString();
            var userId = model.UserId ?? TempData["GoogleUserId"]?.ToString();
            var sessionSecret = model.SessionSecret ?? TempData["GoogleSessionSecret"]?.ToString();

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionSecret))
            {
                return Content($@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <title>Onboarding POST Diagnostic</title>
                        <style>
                            body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; padding: 40px; background: #121214; color: #e1e1e6; line-height: 1.6; }}
                            .card {{ background: #202024; border: 1px solid #323238; padding: 30px; border-radius: 8px; max-width: 800px; margin: 0 auto; box-shadow: 0 4px 12px rgba(0,0,0,0.5); }}
                            h2 {{ color: #FD366E; margin-top: 0; }}
                            code {{ background: #121214; padding: 6px 10px; border-radius: 4px; color: #ff5e5e; font-family: monospace; display: block; word-break: break-all; margin-top: 8px; }}
                            ul {{ padding-left: 20px; }}
                            li {{ margin-bottom: 8px; font-family: monospace; color: #e1e1e6; }}
                            .btn {{ display: inline-block; background: #FD366E; color: white; padding: 10px 20px; text-decoration: none; border-radius: 4px; font-weight: bold; margin-top: 20px; }}
                            .btn:hover {{ background: #e02f60; }}
                        </style>
                    </head>
                    <body>
                        <div class='card'>
                            <h2>[Diagnostic] POST CompleteGoogleSignup Handler</h2>
                            <p>The server received the onboarding form submission, but the required Appwrite session details were missing from the request.</p>
                            <hr style='border: 0; border-top: 1px solid #323238; margin: 20px 0;' />
                            <p><strong>Values Extracted by Backend:</strong></p>
                            <ul>
                                <li><strong>Email:</strong> {(string.IsNullOrEmpty(email) ? "null/empty" : email)}</li>
                                <li><strong>UserId:</strong> {(string.IsNullOrEmpty(userId) ? "null/empty" : userId)}</li>
                                <li><strong>SessionSecret:</strong> {(string.IsNullOrEmpty(sessionSecret) ? "null/empty" : "Present (hidden)")}</li>
                            </ul>
                            <p><strong>Raw Form Data POSTed by Browser ({formFields.Count}):</strong></p>
                            <ul>
                                {(formFields.Count == 0 ? "<li>(No form data detected in the request)</li>" : string.Join("", formFields.Select(f => $"<li>{f}</li>")))}
                            </ul>
                            <p style='color: #888; font-size: 0.9rem; margin-top: 20px;'>This screen is paused so you can inspect the raw post details.</p>
                            <a href='/Auth/Login' class='btn'>Back to Login</a>
                        </div>
                    </body>
                    </html>
                ", "text/html");
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

                // Add the Google user as team owner (the API-key-created team is owned by the app).
                try
                {
                    await _teams.CreateMembership(
                        teamId: teamId,
                        roles: new List<string> { "owner" },
                        email: email,
                        userId: userId,
                        name: name ?? "Google User"
                    );
                }
                catch (AppwriteException mex)
                {
                    Console.WriteLine("[CompleteGoogleSignup] Failed to add user as team owner: " + mex.Message);
                }

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
