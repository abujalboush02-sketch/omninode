using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AgenciesController : ControllerBase
{
    private readonly AppDbContext _context;

    public AgenciesController(AppDbContext context)
    {
        _context = context;
    }

    private (Guid UserId, UserRole Role, Guid? AgencyId) GetCallerContext()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        var agencyClaim = User.FindFirstValue("AgencyId");

        Guid.TryParse(idClaim, out var userId);
        Enum.TryParse<UserRole>(roleClaim, out var role);
        Guid? agencyId = Guid.TryParse(agencyClaim, out var parsedAgencyId) ? parsedAgencyId : null;

        return (userId, role, agencyId);
    }

    /// <summary>
    /// Returns the agency profile, quota usage, and billing health.
    /// </summary>
    [HttpGet("my-agency")]
    public async Task<IActionResult> GetMyAgency()
    {
        var (_, role, agencyId) = GetCallerContext();

        if (role != UserRole.AgencyOwner && role != UserRole.SuperAdmin)
            return Forbid();

        if (!agencyId.HasValue)
            return BadRequest(new { Error = "Caller is not associated with an agency." });

        var agency = await _context.Agencies
            .AsNoTracking()
            .Include(a => a.Tenants)
            .FirstOrDefaultAsync(a => a.Id == agencyId.Value);

        if (agency == null)
            return NotFound();

        return Ok(new
        {
            agency.Id,
            agency.Name,
            agency.OwnerEmail,
            agency.SubscriptionTier,
            agency.BillingStatus,
            agency.NextRenewalDate,
            agency.MaxSubTenants,
            SubTenantCount = agency.Tenants.Count,
            agency.MonthlyTaskQuota,
            agency.TasksUsedThisMonth,
            agency.MonthlyAiQuota,
            agency.AiTasksUsedThisMonth
        });
    }

    /// <summary>
    /// Lists all sub-tenants belonging to the authenticated agency.
    /// </summary>
    [HttpGet("sub-tenants")]
    public async Task<IActionResult> GetSubTenants()
    {
        var (_, role, agencyId) = GetCallerContext();

        if (role != UserRole.AgencyOwner && role != UserRole.SuperAdmin)
            return Forbid();

        if (!agencyId.HasValue)
            return BadRequest(new { Error = "Caller is not associated with an agency." });

        var tenants = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.AgencyId == agencyId.Value)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.OwnerEmail,
                t.MonthlyTaskQuota,
                t.TasksUsedThisMonth,
                t.AiTasksQuota,
                t.AiTasksUsedThisMonth,
                t.BillingStatus,
                EndpointCount = _context.Endpoints.Count(e => e.TenantId == t.Id)
            })
            .ToListAsync();

        return Ok(tenants);
    }

    /// <summary>
    /// Provisions a new sub-tenant under the agency umbrella.
    /// </summary>
    [HttpPost("sub-tenants")]
    public async Task<IActionResult> CreateSubTenant([FromBody] CreateSubTenantRequest request)
    {
        var (_, role, agencyId) = GetCallerContext();

        if (role != UserRole.AgencyOwner && role != UserRole.SuperAdmin)
            return Forbid();

        if (!agencyId.HasValue)
            return BadRequest(new { Error = "Caller is not associated with an agency." });

        var agency = await _context.Agencies
            .Include(a => a.Tenants)
            .FirstOrDefaultAsync(a => a.Id == agencyId.Value);

        if (agency == null)
            return NotFound(new { Error = "Agency not found." });

        if (agency.BillingStatus == "Suspended")
            return StatusCode(402, new { Error = "Agency account is suspended due to unpaid invoices." });

        if (agency.Tenants.Count >= agency.MaxSubTenants)
        {
            return BadRequest(new
            {
                Error = $"Sub-tenant limit reached ({agency.MaxSubTenants}). Please upgrade your agency tier."
            });
        }

        var tenant = new Tenant
        {
            Name = request.Name,
            OwnerEmail = request.OwnerEmail,
            AgencyId = agency.Id,
            TenantType = "AgencySubTenant",
            SubscriptionTier = agency.SubscriptionTier,
            MonthlyTaskQuota = request.AllocatedTaskQuota ?? 5000,
            AiTasksQuota = request.AllocatedAiQuota ?? 200,
            BillingStatus = "Active"
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSubTenants), new { id = tenant.Id }, new
        {
            tenant.Id,
            tenant.Name,
            tenant.OwnerEmail,
            tenant.AgencyId,
            tenant.MonthlyTaskQuota,
            tenant.AiTasksQuota,
            tenant.BillingStatus
        });
    }

    /// <summary>
    /// Updates quota caps for an existing sub-tenant.
    /// </summary>
    [HttpPut("sub-tenants/{subTenantId:guid}/quotas")]
    public async Task<IActionResult> UpdateSubTenantQuotas(Guid subTenantId, [FromBody] UpdateQuotaRequest request)
    {
        var (_, role, agencyId) = GetCallerContext();

        if (role != UserRole.AgencyOwner && role != UserRole.SuperAdmin)
            return Forbid();

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == subTenantId && t.AgencyId == agencyId);

        if (tenant == null)
            return NotFound(new { Error = "Sub-tenant not found within your agency." });

        tenant.MonthlyTaskQuota = request.MonthlyTaskQuota;
        tenant.AiTasksQuota = request.AiTasksQuota;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            tenant.Id,
            tenant.Name,
            tenant.MonthlyTaskQuota,
            tenant.AiTasksQuota
        });
    }
}

public record CreateSubTenantRequest(
    string Name,
    string OwnerEmail,
    int? AllocatedTaskQuota,
    int? AllocatedAiQuota
);

public record UpdateQuotaRequest(
    int MonthlyTaskQuota,
    int AiTasksQuota
);