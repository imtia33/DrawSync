using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DrawSync.Helpers
{
    /// <summary>
    /// Captures the Appwrite session cookie that authenticates a user on subsequent
    /// session-scoped SDK / REST calls.
    ///
    /// WHY THIS EXISTS (verified against the official Appwrite docs + live probing)
    /// --------------------------------------------------------------------
    /// In Appwrite 1.6+ the <c>Account.CreateEmailPasswordSession</c> /
    /// <c>Account.CreateSession</c> endpoints no longer return the session secret in
    /// the JSON response body — <c>Session.Secret</c> is an empty string. Instead the
    /// secret is delivered in the <c>Set-Cookie</c> response header as
    /// <c>a_session_{projectId}=&lt;base64 JSON {"id","secret"}&gt;</c>.
    ///
    /// The Appwrite .NET SDK (<c>Client.SetSession(value)</c>) expects to receive that
    /// full cookie value (or the decoded JSON). Passing the bare secret does NOT
    /// authenticate the client — calls fall back to "guest" role and fail.
    ///
    /// Because the SDK does not expose the underlying HTTP response headers, we perform
    /// the login through a thin raw REST client that lets us read the <c>Set-Cookie</c>
    /// header, extract the <c>a_session_{projectId}</c> cookie, and hand it back. The
    /// caller stores it in the <c>AppwriteSession</c> claim so that every per-request
    /// scoped <c>Client</c> (see <see cref="DrawSync.Program"/>) is authenticated as the
    /// user for the rest of the session.
    /// </summary>
    public static class SessionCookieHelper
    {
        /// <summary>
        /// Creates an email/password session and returns the session cookie value
        /// suitable for <c>Client.SetSession(...)</c>. Returns null on failure.
        /// </summary>
        public static async Task<string?> CreateEmailPasswordSessionAsync(
            string endpoint, string projectId, string email, string password)
        {
            using var http = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/account/sessions/email")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "email", email },
                    { "password", password }
                })
            };
            req.Headers.Add("X-Appwrite-Project", projectId);

            var resp = await http.SendAsync(req);
            // 201 = created. We don't strictly require the body — only the cookie.
            return ExtractSessionCookie(resp, projectId);
        }

        /// <summary>
        /// Exchanges an OAuth2 userId+secret pair (from the Google OAuth callback) for a
        /// session cookie value. Returns null on failure.
        /// </summary>
        public static async Task<string?> CreateSessionFromOAuthAsync(
            string endpoint, string projectId, string userId, string secret)
        {
            using var http = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/account/sessions")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "userId", userId },
                    { "secret", secret }
                })
            };
            req.Headers.Add("X-Appwrite-Project", projectId);

            var resp = await http.SendAsync(req);
            return ExtractSessionCookie(resp, projectId);
        }

        /// <summary>
        /// Pulls the <c>a_session_{projectId}</c> cookie value out of the Set-Cookie
        /// header. Appwrite sets both a legacy and a non-legacy cookie; we want the
        /// non-legacy one (the value the SDK's <c>SetSession</c> accepts).
        /// </summary>
        private static string? ExtractSessionCookie(HttpResponseMessage resp, string projectId)
        {
            IEnumerable<string>? cookies;
            if (!resp.Headers.TryGetValues("Set-Cookie", out cookies) || cookies == null)
                return null;

            var name = $"a_session_{projectId}=";
            var legacy = $"a_session_{projectId}_legacy=";

            // Prefer the non-legacy cookie. The SDK accepts either the raw base64 cookie
            // value or the decoded JSON, but the raw cookie value is what works with
            // Client.SetSession in practice.
            string? chosen = null;
            foreach (var c in cookies)
            {
                if (c.StartsWith(name) && !c.StartsWith(legacy))
                {
                    var rest = c.Substring(name.Length);
                    var semi = rest.IndexOf(';');
                    chosen = semi >= 0 ? rest.Substring(0, semi) : rest;
                    break;
                }
            }

            // Fall back to the legacy cookie if the non-legacy one is missing for some reason.
            if (string.IsNullOrEmpty(chosen))
            {
                foreach (var c in cookies)
                {
                    if (c.StartsWith(legacy))
                    {
                        var rest = c.Substring(legacy.Length);
                        var semi = rest.IndexOf(';');
                        chosen = semi >= 0 ? rest.Substring(0, semi) : rest;
                        break;
                    }
                }
            }

            return chosen;
        }
    }
}
