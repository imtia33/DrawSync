using DrawSync.UnitOfWork.Interface;
using DrawSync.Models;
using DrawSync.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Appwrite;
using Appwrite.Services;
using DrawSync.Filters;
using Usage = DrawSync.Models.Usage;
using Organization = DrawSync.Models.Organization;

namespace DrawSync.Controllers
{
    [Authorize]
    [VerifiedUser]
    [Route("api/organization/{organizationId}")]
    [ApiController]
    public class OrganizationDashboardController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Teams _teams;
        private readonly Presences _presences;

        public OrganizationDashboardController(IUnitOfWork unitOfWork, Teams teams, Presences presences)
        {
            _unitOfWork = unitOfWork;
            _teams = teams;
            _presences = presences;
        }

        private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private async Task<bool> IsUserInOrgAsync(string organizationId)
        {
            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            return org != null;
        }

        // Dashboard Overview - List Drawings
        [HttpGet("drawings")]
        public async Task<ActionResult<IEnumerable<Drawing>>> GetDrawings(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var drawings = await _unitOfWork.Drawings.GetByOrganizationAsync(organizationId);
            return Ok(drawings);
        }

        // Create Drawing - WITH PLAN LIMIT ENFORCEMENT
        [HttpPost("drawings")]
        public async Task<ActionResult<Drawing>> CreateDrawing(string organizationId, [FromBody] CreateDrawingRequest req)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            if (org == null)
                return NotFound();

            // Enforce plan limits - STRICT CHECK
            var currentDrawings = await _unitOfWork.Drawings.GetByOrganizationAsync(organizationId);
            if (!PlanLimits.CanCreateDrawing(org.Plan, currentDrawings.Count()))
            {
                var (maxDrawings, _) = PlanLimits.GetLimits(org.Plan);
                return BadRequest(new { error = $"Plan limit reached. {org.Plan} plan allows {maxDrawings} drawing(s). Upgrade to Pro for more." });
            }

            var userId = GetUserId();
            var drawing = new Drawing
            {
                Id = ID.Unique(),
                OrganizationId = organizationId,
                Name = req.Name,
                Type = req.Type ?? "whiteboard"
            };

            await _unitOfWork.Drawings.AddAsync(drawing, new List<string> {
                Permission.Read(Role.Team(organizationId)),
                Permission.Update(Role.Team(organizationId)),
                Permission.Delete(Role.Team(organizationId))
            });
            await UpdateUsageOnDrawingCreate(organizationId, currentDrawings.Count() + 1);

            await _unitOfWork.SaveChangesAsync();
            return CreatedAtAction(nameof(GetDrawing), new { organizationId, id = drawing.Id }, drawing);
        }

        // Get Single Drawing
        [HttpGet("drawings/{id}")]
        public async Task<ActionResult<Drawing>> GetDrawing(string organizationId, string id)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var drawing = await _unitOfWork.Drawings.GetByIdAsync(id);
            if (drawing?.OrganizationId != organizationId)
                return NotFound();

            return Ok(drawing);
        }

        // Update Drawing
        [HttpPut("drawings/{id}")]
        public async Task<IActionResult> UpdateDrawing(string organizationId, string id, [FromBody] UpdateDrawingRequest req)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var drawing = await _unitOfWork.Drawings.GetByIdAsync(id);
            if (drawing?.OrganizationId != organizationId)
                return NotFound();

            drawing.Name = req.Name ?? drawing.Name;

            await _unitOfWork.Drawings.UpdateAsync(id, drawing);
            await _unitOfWork.SaveChangesAsync();
            return NoContent();
        }

        // Delete Drawing
        [HttpDelete("drawings/{id}")]
        public async Task<IActionResult> DeleteDrawing(string organizationId, string id)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var drawing = await _unitOfWork.Drawings.GetByIdAsync(id);
            if (drawing?.OrganizationId != organizationId)
                return NotFound();

            await _unitOfWork.Drawings.DeleteAsync(id);
            var currentDrawings = await _unitOfWork.Drawings.GetByOrganizationAsync(organizationId);
            await UpdateUsageOnDrawingDelete(organizationId, currentDrawings.Count());

            await _unitOfWork.SaveChangesAsync();
            return NoContent();
        }

        // Members - List (with online presence)
        [HttpGet("members")]
        public async Task<ActionResult> ListMembers(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            try
            {
                var teamMembers = await _teams.ListMemberships(organizationId);

                // Fetch active presences for this team
                var onlineUserIds = new HashSet<string>();
                try
                {
                    var presences = await _presences.List(
                        queries: new List<string> { Query.Equal("status", "online") }
                    );
                    foreach (var p in presences.Presences)
                    {
                        // Check if this presence belongs to the current organization
                        if (p.Id.StartsWith($"{organizationId}_"))
                        {
                            onlineUserIds.Add(p.UserId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log warning/error (e.g., if API key lacks "presences.read" scope)
                    Console.WriteLine($"[Warning] Failed to fetch online presences: {ex.Message}");
                }

                // Enrich memberships with online status
                var enrichedMembers = teamMembers.Memberships.Select(m => new
                {
                    m.Id,
                    m.UserId,
                    m.UserName,
                    UserEmail = m.UserEmail,
                    m.Roles,
                    Confirm = m.Confirm,
                    m.Mfa,
                    CreatedAt = m.CreatedAt,
                    IsOnline = onlineUserIds.Contains(m.UserId)
                }).ToList();

                return Ok(new { memberships = enrichedMembers, total = enrichedMembers.Count });
            }
            catch (AppwriteException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Presence - Heartbeat (upsert user presence)
        [HttpPut("presence")]
        public async Task<IActionResult> UpsertPresence(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            try
            {
                var userName = User.Identity?.Name ?? "Unknown";
                var presenceId = $"{organizationId}_{userId}";

                await _presences.Upsert(
                    presenceId: presenceId,
                    userId: userId,
                    status: "online",
                    permissions: new List<string>
                    {
                        Permission.Read(Role.Team(organizationId))
                    },
                    expiresAt: DateTime.UtcNow.AddMinutes(2).ToString("o"),
                    metadata: new Dictionary<string, object>
                    {
                        { "userName", userName },
                        { "organizationId", organizationId }
                    }
                );

                return Ok(new { status = "online" });
            }
            catch (AppwriteException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Members - Invite using Teams API - WITH PLAN LIMIT ENFORCEMENT
        [HttpPost("members/invite")]
        public async Task<IActionResult> InviteMember(string organizationId, [FromBody] InviteMemberRequest req)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            if (org == null)
                return NotFound();

            try
            {
                // Enforce plan member limits - STRICT CHECK
                var members = await _teams.ListMemberships(organizationId);
                if (!PlanLimits.CanAddMember(org.Plan, members.Memberships.Count))
                {
                    var (_, maxMembers) = PlanLimits.GetLimits(org.Plan);
                    return BadRequest(new { error = $"Plan limit reached. {org.Plan} plan allows {maxMembers} member(s). Remove members or upgrade to Pro." });
                }

                // Invite via Teams API - sends email automatically
                var membership = await _teams.CreateMembership(
                    teamId: organizationId,
                    roles: new List<string> { req.Role ?? "member" },
                    email: req.Email
                );

                // Auto-update Usage record with new member count
                var updatedMembers = await _teams.ListMemberships(organizationId);
                await UpdateUsageOnMemberChange(organizationId, updatedMembers.Memberships.Count);

                return Ok(new { message = "Member invited successfully. Email sent.", membershipId = membership.Id });
            }
            catch (AppwriteException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Members - Remove
        [HttpDelete("members/{membershipId}")]
        public async Task<IActionResult> RemoveMember(string organizationId, string membershipId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            try
            {
                await _teams.DeleteMembership(organizationId, membershipId);

                // Auto-update Usage record with new member count
                var updatedMembers = await _teams.ListMemberships(organizationId);
                await UpdateUsageOnMemberChange(organizationId, updatedMembers.Memberships.Count);

                return NoContent();
            }
            catch (AppwriteException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Billing - Get Plan
        [HttpGet("billing")]
        public async Task<ActionResult> GetBilling(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            if (org == null)
                return NotFound();

            var (maxDrawings, maxMembers) = PlanLimits.GetLimits(org.Plan);
            return Ok(new
            {
                plan = org.Plan,
                name = org.Name,
                limits = new { drawings = maxDrawings, members = maxMembers }
            });
        }

        // Billing - Upgrade to Pro - SECURE (server-side only)
        [HttpPost("billing/upgrade")]
        public async Task<IActionResult> UpgradeToPro(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            if (org == null)
                return NotFound();

            // SECURITY: Prevent double-upgrades
            if (org.Plan == "pro")
                return BadRequest(new { error = "Already on Pro plan." });

            org.Plan = "pro";
            await _unitOfWork.Organizations.UpdateAsync(organizationId, org);

            // Auto-generate invoice for upgrade
            var invoice = new Invoice
            {
                Id = ID.Unique(),
                OrganizationId = organizationId,
                Amount = 9.99m,
                Currency = "USD",
                Status = "paid",
                Period = DateTime.UtcNow.ToString("yyyy-MM")
            };
            await _unitOfWork.Invoices.AddAsync(invoice, new List<string> {
                Permission.Read(Role.Team(organizationId))
            });

            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "Organization upgraded to Pro", newPlan = org.Plan, success = true, invoiceId = invoice.Id });
        }

        // Settings - Get Organization Details (read-only for users)
        [HttpGet("settings")]
        public async Task<ActionResult> GetSettings(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            if (org == null)
                return NotFound();

            var (maxDrawings, maxMembers) = PlanLimits.GetLimits(org.Plan);
            var drawings = await _unitOfWork.Drawings.GetByOrganizationAsync(organizationId);
            var members = await _teams.ListMemberships(organizationId);

            return Ok(new
            {
                org,
                usage = new
                {
                    drawings = drawings.Count(),
                    maxDrawings,
                    members = members.Memberships.Count,
                    maxMembers
                }
            });
        }

        // Settings - Update Organization (name only, other fields server-controlled)
        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings(string organizationId, [FromBody] UpdateOrgRequest req)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            if (org == null)
                return NotFound();

            org.Name = req.Name ?? org.Name;
            await _unitOfWork.Organizations.UpdateAsync(organizationId, org);
            await _unitOfWork.SaveChangesAsync();

            return Ok(org);
        }

        // Invoices - List
        [HttpGet("invoices")]
        public async Task<ActionResult<IEnumerable<Invoice>>> GetInvoices(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var invoices = await _unitOfWork.Invoices.GetByOrganizationAsync(organizationId);
            return Ok(invoices);
        }

        // Usage - Get Current Stats
        [HttpGet("usage")]
        public async Task<ActionResult> GetUsage(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            if (org == null)
                return NotFound();

            var drawings = await _unitOfWork.Drawings.GetByOrganizationAsync(organizationId);
            var members = await _teams.ListMemberships(organizationId);
            var usage = await _unitOfWork.Usage.GetCurrentMonthAsync(organizationId);

            var (maxDrawings, maxMembers) = PlanLimits.GetLimits(org.Plan);

            return Ok(new
            {
                drawingsCount = drawings.Count(),
                maxDrawings,
                collaborators = members.Memberships.Count,
                maxMembers,
                renewDate = usage?.RenewDate ?? DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd")
            });
        }

        // Settings - Delete Organization
        [HttpDelete("")]
        public async Task<IActionResult> DeleteOrganization(string organizationId)
        {
            if (!await IsUserInOrgAsync(organizationId))
                return Forbid();

            var org = await _unitOfWork.Organizations.GetByIdAsync(organizationId);
            if (org == null)
                return NotFound();

            // Delete all associated drawings
            var drawings = await _unitOfWork.Drawings.GetByOrganizationAsync(organizationId);
            foreach (var drawing in drawings)
            {
                await _unitOfWork.Drawings.DeleteAsync(drawing.Id ?? string.Empty);
            }

            // Delete the team from Appwrite
            try
            {
                await _teams.Delete(organizationId);
            }
            catch { /* Team may not exist */ }

            // Delete the organization
            await _unitOfWork.Organizations.DeleteAsync(organizationId);

            await _unitOfWork.SaveChangesAsync();
            return Ok(new { message = "Organization deleted successfully" });
        }

        // Helper: Auto-update Usage when drawing is created
        private async Task UpdateUsageOnDrawingCreate(string organizationId, int newCount)
        {
            var usage = await _unitOfWork.Usage.GetCurrentMonthAsync(organizationId);
            if (usage == null)
            {
                usage = new Usage
                {
                    Id = ID.Unique(),
                    OrganizationId = organizationId,
                    DrawingsCount = newCount,
                    Collaborators = 1,
                    RenewDate = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd")
                };
                await _unitOfWork.Usage.AddAsync(usage, new List<string> {
                    Permission.Read(Role.Team(organizationId)),
                    Permission.Update(Role.Team(organizationId))
                });
            }
            else
            {
                usage.DrawingsCount = newCount;
                await _unitOfWork.Usage.UpdateAsync(usage.Id ?? string.Empty, usage);
            }
        }

        // Helper: Auto-update Usage when drawing is deleted
        private async Task UpdateUsageOnDrawingDelete(string organizationId, int newCount)
        {
            var usage = await _unitOfWork.Usage.GetCurrentMonthAsync(organizationId);
            if (usage != null)
            {
                usage.DrawingsCount = newCount;
                await _unitOfWork.Usage.UpdateAsync(usage.Id ?? string.Empty, usage);
            }
        }

        // Helper: Auto-update Usage when member count changes
        private async Task UpdateUsageOnMemberChange(string organizationId, int newMemberCount)
        {
            var usage = await _unitOfWork.Usage.GetCurrentMonthAsync(organizationId);
            if (usage == null)
            {
                var drawings = await _unitOfWork.Drawings.GetByOrganizationAsync(organizationId);
                usage = new Usage
                {
                    Id = ID.Unique(),
                    OrganizationId = organizationId,
                    DrawingsCount = drawings.Count(),
                    Collaborators = newMemberCount,
                    RenewDate = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd")
                };
                await _unitOfWork.Usage.AddAsync(usage, new List<string> {
                    Permission.Read(Role.Team(organizationId)),
                    Permission.Update(Role.Team(organizationId))
                });
            }
            else
            {
                usage.Collaborators = newMemberCount;
                await _unitOfWork.Usage.UpdateAsync(usage.Id ?? string.Empty, usage);
            }
        }

        // Request DTOs
        public class CreateDrawingRequest
        {
            public string Name { get; set; } = null!;
            public string? Type { get; set; }
        }

        public class UpdateDrawingRequest
        {
            public string? Name { get; set; }
        }

        public class InviteMemberRequest
        {
            public string Email { get; set; } = null!;
            public string? Role { get; set; }
        }

        public class UpdateOrgRequest
        {
            public string? Name { get; set; }
        }

    } // closes class OrganizationDashboardController
} // closes namespace DrawSync.Controllers