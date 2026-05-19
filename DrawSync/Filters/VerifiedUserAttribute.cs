using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Security.Claims;

namespace DrawSync.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class VerifiedUserAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var isVerifiedClaim = user.FindFirst("IsVerified")?.Value;
                if (isVerifiedClaim != "true")
                {
                    // Check if it is an API/JSON request (e.g. ApiControllers or routes starting with /api/)
                    var isApi = context.ActionDescriptor.DisplayName?.Contains("ApiController") == true ||
                                context.HttpContext.Request.Path.Value?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true;

                    if (isApi)
                    {
                        context.Result = new ObjectResult(new { error = "Email verification required. Please verify your email first." })
                        {
                            StatusCode = 403
                        };
                    }
                    else
                    {
                        context.Result = new RedirectToActionResult("VerificationPending", "Auth", null);
                    }
                }
            }
            base.OnActionExecuting(context);
        }
    }
}
