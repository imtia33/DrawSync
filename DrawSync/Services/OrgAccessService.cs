using Appwrite;
using Appwrite.Services;
using Appwrite.Models;
using DrawSync.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Security.Claims;
// Disambiguate model names that collide with Appwrite.Services types.
using Organization = DrawSync.Models.Organization;
using Drawing = DrawSync.Models.Drawing;

namespace DrawSync.Services
{
    /// <summary>
    /// User-scoped access service for organizations / drawings / team roles.
    ///
    /// SECURITY MODEL (verified against the official Appwrite docs + live probing)
    /// --------------------------------------------------------------------
    /// Appwrite supports two independent permission layers:
    ///   1. Table-level permissions  — apply to EVERY row in the table.
    ///   2. Row-level permissions    — apply to individual rows, but ONLY take effect
    ///                                 when "Row Security" is enabled on the table.
    /// Per the docs: "A user with access granted at either table level or row level
    /// will be able to access a row. Users don't need access at both levels."
    ///
    /// In THIS project the <c>organization</c> and <c>drawings</c> tables are configured
    /// with table-level <c>read("users")</c> (i.e. any authenticated user can read every
    /// row at the table level), even though each row also carries a stricter
    /// <c>read("team:{teamId}")</c>. Because table-level access is sufficient, a plain
    /// session-scoped <c>TablesDB.ListRows</c> returns EVERY org/drawing on the platform
    /// to any logged-in user — the row-level team permission is bypassed.
    ///
    /// THEREFORE we cannot rely on Appwrite row permissions to filter listings. Instead
    /// we use <c>Teams.List()</c> (which IS properly session-scoped — it returns only the
    /// teams the user belongs to) as the single source of truth for membership, and
    /// cross-check every row's <c>$id</c> against that list. This is defense-in-depth:
    /// even if the table config changes, listings stay user-scoped.
    ///
    /// The session cookie: in Appwrite 1.6+ <c>Session.Secret</c> is empty; the secret
    /// is delivered via the <c>Set-Cookie</c> header (<c>a_session_{projectId}</c>).
    /// <c>Client.SetSession(value)</c> expects that full cookie value. The
    /// <c>AppwriteSession</c> claim therefore stores the cookie value, not a bare secret.
    /// </summary>
    public interface IOrgAccessService
    {
        /// <summary>Appwrite account id of the currently signed-in user (from claims).</summary>
        string? GetCurrentUserId();

        /// <summary>Lists the organizations (teams) the current user is a member of, via session-scoped Teams.List().</summary>
        Task<List<Organization>> GetCurrentUserOrganizationsAsync();

        /// <summary>Lists the organizations for an explicit session cookie value (used mid-login, before claims are refreshed).</summary>
        Task<List<Organization>> GetOrganizationsForSessionAsync(string sessionCookie);

        /// <summary>Team ids the current user belongs to (session-scoped Teams.List()).</summary>
        Task<List<string>> GetCurrentUserTeamIdsAsync();

        /// <summary>True if the current user is a member of the org/team with the given id.</summary>
        Task<bool> IsCurrentUserMemberOfOrgAsync(string organizationId);

        /// <summary>True if the current user is an admin/owner of the org/team with the given id.</summary>
        Task<bool> IsCurrentUserOrgAdminAsync(string organizationId);

        /// <summary>
        /// Lists drawings for the given org using a session-scoped TablesDB (auto-filtered by row
        /// permissions) AND explicitly filtered by organizationId. Non-members get an empty list.
        /// </summary>
        Task<List<Drawing>> GetDrawingsForOrgAsync(string organizationId);

        /// <summary>Fetch a single drawing, but ONLY if the current user is a member of its org.</summary>
        Task<Drawing?> GetDrawingIfMemberAsync(string drawingId);
    }

    public class OrgAccessService : IOrgAccessService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _config;
        private readonly Teams _adminTeams;
        private readonly ILogger<OrgAccessService> _logger;

        private string Endpoint => _config["Appwrite:Endpoint"]!;
        private string Project => _config["Appwrite:Project"]!;
        private string DatabaseId => _config["Appwrite:DatabaseId"]!;
        private string OrgTable => _config["Appwrite:Tables:Organization"]!;
        private string DrawingTable => _config["Appwrite:Tables:Drawing"]!;

        public OrgAccessService(
            IHttpContextAccessor httpContextAccessor,
            IConfiguration config,
            Teams adminTeams,
            ILogger<OrgAccessService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _config = config;
            _adminTeams = adminTeams;
            _logger = logger;
        }

        public string? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.User?
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        /// <summary>The session secret for the current request, pulled from the AppwriteSession claim.</summary>
        private string? GetCurrentSessionSecret()
        {
            return _httpContextAccessor.HttpContext?.User?
                .FindFirst("AppwriteSession")?.Value;
        }

        /// <summary>Build an Appwrite Client authenticated as the user via a session secret.</summary>
        private Client BuildSessionClient(string sessionSecret)
        {
            var client = new Client()
                .SetEndpoint(Endpoint)
                .SetProject(Project)
                .SetSession(sessionSecret);
            return client;
        }

        public async Task<List<string>> GetCurrentUserTeamIdsAsync()
        {
            var cookie = GetCurrentSessionSecret();
            if (string.IsNullOrEmpty(cookie))
            {
                _logger.LogWarning("GetCurrentUserTeamIdsAsync: no AppwriteSession claim present.");
                return new List<string>();
            }

            try
            {
                var teams = new Teams(BuildSessionClient(cookie));
                var list = await teams.List();
                return list.Teams.Select(t => t.Id).ToList();
            }
            catch (AppwriteException ex)
            {
                _logger.LogError(ex, "Failed to list current user's teams (code={Code}, type={Type}).", ex.Code, ex.Type);
                return new List<string>();
            }
        }

        public async Task<List<Organization>> GetCurrentUserOrganizationsAsync()
        {
            var cookie = GetCurrentSessionSecret();
            if (string.IsNullOrEmpty(cookie))
            {
                _logger.LogWarning("GetCurrentUserOrganizationsAsync: no AppwriteSession claim present.");
                return new List<Organization>();
            }
            return await ListOrgsForSession(cookie);
        }

        public async Task<List<Organization>> GetOrganizationsForSessionAsync(string sessionCookie)
        {
            Console.WriteLine($"[OrgAccess] GetOrganizationsForSessionAsync called, cookie len={sessionCookie?.Length ?? -1}");
            if (string.IsNullOrEmpty(sessionCookie))
            {
                Console.WriteLine("[OrgAccess] GetOrganizationsForSessionAsync: empty cookie, returning empty");
                return new List<Organization>();
            }
            return await ListOrgsForSession(sessionCookie);
        }

        /// <summary>
        /// List the org rows the current user may access.
        ///
        /// Because the <c>organization</c> table has table-level <c>read("users")</c>, a
        /// session-scoped <c>TablesDB.ListRows</c> returns EVERY org row to any logged-in
        /// user. We therefore use <c>Teams.List()</c> (properly session-scoped) as the
        /// authority on which orgs the user belongs to, and filter the org rows by
        /// <c>$id ∈ teamIds</c>. Non-members get an empty list.
        /// </summary>
        private async Task<List<Organization>> ListOrgsForSession(string sessionCookie)
        {
            // 1. Authority: which teams does this user actually belong to?
            HashSet<string> myTeamIds;
            try
            {
                var teams = new Teams(BuildSessionClient(sessionCookie));
                var teamList = await teams.List();
                myTeamIds = teamList.Teams.Select(t => t.Id).ToHashSet();
                _logger.LogInformation("ListOrgsForSession: user belongs to {Count} team(s): [{Teams}]",
                    myTeamIds.Count, string.Join(",", myTeamIds));
            }
            catch (AppwriteException ex)
            {
                _logger.LogError(ex, "ListOrgsForSession: Teams.List failed (code={Code}). Aborting — returning empty.", ex.Code);
                return new List<Organization>();
            }

            if (myTeamIds.Count == 0)
            {
                // User is in no teams — they can legitimately access zero orgs. Skip the
                // tables query entirely (defense-in-depth: don't even fetch rows we'd drop).
                return new List<Organization>();
            }

            // 2. Fetch org rows (session-scoped, but over-permissive due to table-level read).
            try
            {
                var tables = new TablesDB(BuildSessionClient(sessionCookie));
                var result = await tables.ListRows(
                    databaseId: DatabaseId,
                    tableId: OrgTable,
                    queries: new List<string> { Query.Limit(100), Query.OrderDesc("$createdAt") }
                );

                // 3. DEFENSE-IN-DEPTH: keep only rows whose $id is a team the user belongs to.
                var orgs = new List<Organization>();
                int dropped = 0;
                foreach (var row in result.Rows)
                {
                    var rowId = row.Data.ContainsKey("$id") ? row.Data["$id"]?.ToString() : row.Id;
                    if (rowId != null && myTeamIds.Contains(rowId))
                    {
                        var json = JsonConvert.SerializeObject(row.Data);
                        var org = JsonConvert.DeserializeObject<Organization>(json);
                        if (org != null) orgs.Add(org);
                    }
                    else
                    {
                        dropped++;
                    }
                }
                _logger.LogInformation("ListOrgsForSession: fetched {Fetched} row(s), kept {Kept}, dropped {Dropped} (not a member).",
                    result.Rows.Count, orgs.Count, dropped);
                return orgs;
            }
            catch (AppwriteException ex)
            {
                _logger.LogError(ex, "ListOrgsForSession: TablesDB.ListRows failed (code={Code}).", ex.Code);
                return new List<Organization>();
            }
        }

        public async Task<bool> IsCurrentUserMemberOfOrgAsync(string organizationId)
        {
            if (string.IsNullOrEmpty(organizationId)) return false;
            var teamIds = await GetCurrentUserTeamIdsAsync();
            var isMember = teamIds.Contains(organizationId);
            if (!isMember)
            {
                _logger.LogWarning("Membership DENIED: user {UserId} is NOT a member of org {OrgId}.",
                    GetCurrentUserId() ?? "?", organizationId);
            }
            return isMember;
        }

        public async Task<bool> IsCurrentUserOrgAdminAsync(string organizationId)
        {
            if (string.IsNullOrEmpty(organizationId)) return false;

            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return false;

            // First ensure the user is at least a member.
            if (!await IsCurrentUserMemberOfOrgAsync(organizationId)) return false;

            try
            {
                // Use the admin Teams service to read the team's memberships and find the
                // current user's roles. This is a legitimate server-side authorization check.
                var memberships = await _adminTeams.ListMemberships(organizationId);
                var mine = memberships.Memberships.FirstOrDefault(m => m.UserId == userId);
                if (mine == null)
                {
                    _logger.LogWarning("IsCurrentUserOrgAdminAsync: user {UserId} has no membership row in org {OrgId}.", userId, organizationId);
                    return false;
                }

                var isAdmin = mine.Roles != null && (
                    mine.Roles.Contains("owner", StringComparer.OrdinalIgnoreCase) ||
                    mine.Roles.Contains("admin", StringComparer.OrdinalIgnoreCase));

                _logger.LogInformation("IsCurrentUserOrgAdminAsync: user {UserId} in org {OrgId} roles=[{Roles}] → admin={IsAdmin}.",
                    userId, organizationId, mine.Roles == null ? "" : string.Join(",", mine.Roles), isAdmin);
                return isAdmin;
            }
            catch (AppwriteException ex)
            {
                _logger.LogError(ex, "IsCurrentUserOrgAdminAsync failed for org {OrgId} (code={Code}).", organizationId, ex.Code);
                return false;
            }
        }

        public async Task<List<Drawing>> GetDrawingsForOrgAsync(string organizationId)
        {
            if (string.IsNullOrEmpty(organizationId)) return new List<Drawing>();

            var secret = GetCurrentSessionSecret();
            if (string.IsNullOrEmpty(secret))
            {
                _logger.LogWarning("GetDrawingsForOrgAsync: no AppwriteSession claim.");
                return new List<Drawing>();
            }

            // Defense in depth: verify membership before listing.
            if (!await IsCurrentUserMemberOfOrgAsync(organizationId))
            {
                return new List<Drawing>();
            }

            try
            {
                var tables = new TablesDB(BuildSessionClient(secret));
                var result = await tables.ListRows(
                    databaseId: DatabaseId,
                    tableId: DrawingTable,
                    queries: new List<string>
                    {
                        Query.Equal("organizationId", organizationId),
                        Query.Limit(100),
                        Query.OrderDesc("$createdAt")
                    }
                );

                var drawings = new List<Drawing>();
                foreach (var row in result.Rows)
                {
                    var json = JsonConvert.SerializeObject(row.Data);
                    var d = JsonConvert.DeserializeObject<Drawing>(json);
                    if (d != null) drawings.Add(d);
                }
                return drawings;
            }
            catch (AppwriteException ex)
            {
                _logger.LogError(ex, "GetDrawingsForOrgAsync failed for org {OrgId} (code={Code}).", organizationId, ex.Code);
                return new List<Drawing>();
            }
        }

        public async Task<Drawing?> GetDrawingIfMemberAsync(string drawingId)
        {
            if (string.IsNullOrEmpty(drawingId)) return null;

            var cookie = GetCurrentSessionSecret();
            if (string.IsNullOrEmpty(cookie)) return null;

            try
            {
                var tables = new TablesDB(BuildSessionClient(cookie));
                // Fetch the row (session-scoped, but table-level read("users") lets any
                // authenticated user read it — so we MUST verify membership below).
                var row = await tables.GetRow(
                    databaseId: DatabaseId,
                    tableId: DrawingTable,
                    rowId: drawingId
                );
                if (row?.Data == null) return null;

                var json = JsonConvert.SerializeObject(row.Data);
                var drawing = JsonConvert.DeserializeObject<Drawing>(json);
                if (drawing == null) return null;

                // DEFENSE-IN-DEPTH: confirm the user is a member of the drawing's org team.
                // We cannot trust Appwrite row permissions here because the table has
                // table-level read("users"). The membership check uses Teams.List().
                if (string.IsNullOrEmpty(drawing.OrganizationId) ||
                    !await IsCurrentUserMemberOfOrgAsync(drawing.OrganizationId))
                {
                    _logger.LogWarning("GetDrawingIfMemberAsync: DENIED — user {UserId} is not a member of org {OrgId} for drawing {DrawingId}.",
                        GetCurrentUserId() ?? "?", drawing.OrganizationId, drawingId);
                    return null;
                }

                return drawing;
            }
            catch (AppwriteException ex)
            {
                _logger.LogWarning(ex, "GetDrawingIfMemberAsync: drawing {DrawingId} not accessible by user {UserId} (code={Code}).",
                    drawingId, GetCurrentUserId() ?? "?", ex.Code);
                return null;
            }
        }
    }
}
