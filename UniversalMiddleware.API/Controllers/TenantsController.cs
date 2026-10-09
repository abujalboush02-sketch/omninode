using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;
using UniversalMiddleware.API.Helpers;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class TenantsController : ControllerBase {
    private readonly AppDbContext _context;

    public TenantsController(AppDbContext context) {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant([FromBody] Tenant tenant) {
        tenant.TasksUsedThisMonth = 0;
        
        switch (tenant.SubscriptionTier) {
            case "Starter": tenant.MonthlyTaskQuota = 1000; break;
            case "Pro": tenant.MonthlyTaskQuota = 10000; break;
            case "Enterprise": tenant.MonthlyTaskQuota = 100000; break;
            default: tenant.MonthlyTaskQuota = 1000; break;
        }

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        return Ok(tenant);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetTenant(Guid id) {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();
        return Ok(tenant);
    }

    [HttpPost("{id}/generate-key")]
    public async Task<IActionResult> GenerateKey(Guid id)
    {
        if (HttpContext.GetTenantId() != id)
            return StatusCode(403, new { Error = "You can only rotate your own API key." });

        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound("Tenant not found.");

        var (raw, hash) = ApiKeys.Generate();
        tenant.ApiKeyHash = hash;
        await _context.SaveChangesAsync();

        return Ok(new { ApiKey = raw, Message = "Please save this API key securely. It will not be shown again." });
    }
}
