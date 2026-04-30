using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

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
    public async Task<IActionResult> GenerateKey(Guid id) {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound("Tenant not found.");

        // Generate a secure 32-character API key
        var rawKey = "um_live_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace("+", "").Replace("/", "").Substring(0, 24);

        // Hash it
        using (var sha256 = SHA256.Create()) {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
            tenant.ApiKeyHash = Convert.ToBase64String(hashedBytes);
        }

        await _context.SaveChangesAsync();

        // Return the raw key exactly once. The user must save this, as the system only stores the hash and cannot retrieve the raw key again.
        return Ok(new { ApiKey = rawKey, Message = "Please save this API key securely. It will not be shown again." });
    }
}
