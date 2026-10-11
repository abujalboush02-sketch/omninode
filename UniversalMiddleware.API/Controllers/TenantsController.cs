using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public record CreateTenantRequest(string Name, string OwnerEmail, string? OwnerName, string? PlanId);

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _context;

    public TenantsController(AppDbContext context)
    {
        _context = context;
    }

    private bool IsSuperAdmin() =>
        HttpContext.Items.TryGetValue("AuthRole", out var r) && r is UserRole role && role == UserRole.SuperAdmin;

    /// <summary>SuperAdmin only. Self-serve tenants come from /api/Auth/register.</summary>
    [HttpPost]
    [ApiKeyAuth(UserRole.SuperAdmin)]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
    {
        var plan = Plans.Find(request.PlanId) ?? Plans.Find("free")!;
        var (raw, hash) = ApiKeys.Generate();

        var tenant = new Tenant
        {
            Name = request.Name,
            OwnerEmail = request.OwnerEmail,
            OwnerName = request.OwnerName ?? request.Name,
            PlanType = "Standalone",
            TenantType = "Standalone",
            BillingStatus = "Active",
            ApiKeyHash = hash,
            TasksUsedThisMonth = 0
        };
        Plans.Apply(tenant, plan);

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        return Ok(new { Tenant = ToDto(tenant), ApiKey = raw });
    }

    /// <summary>Own tenant only (SuperAdmin can read any). Never returns the key hash.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTenant(Guid id)
    {
        if (!IsSuperAdmin() && HttpContext.GetTenantId() != id)
            return StatusCode(403, new { Error = "You can only view your own workspace." });

        var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        return tenant == null ? NotFound() : Ok(ToDto(tenant));
    }

    [HttpPost("{id:guid}/generate-key")]
    public async Task<IActionResult> GenerateKey(Guid id)
    {
        if (HttpContext.GetTenantId() != id)
            return StatusCode(403, new { Error = "You can only rotate your own API key." });

        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound(new { Error = "Tenant not found." });

        var (raw, hash) = ApiKeys.Generate();
        tenant.ApiKeyHash = hash;
        await _context.SaveChangesAsync();

        return Ok(new { ApiKey = raw, Message = "Please save this API key securely. It will not be shown again." });
    }

    private static object ToDto(Tenant t) => new
    {
        t.Id,
        t.Name,
        t.OwnerName,
        t.OwnerEmail,
        t.SubscriptionTier,
        PlanId = Plans.Find(t.SubscriptionTier)?.Id,
        t.TenantType,
        t.AgencyId,
        t.BillingStatus,
        t.NextRenewalDate,
        t.MonthlyTaskQuota,
        t.TasksUsedThisMonth,
        t.AiTasksQuota,
        t.AiTasksUsedThisMonth,
        IsFree = Plans.IsFree(t)
    };
}
