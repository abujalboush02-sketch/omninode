using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class CredentialsController : ControllerBase
{
    private readonly AppDbContext _context;

    public CredentialsController(AppDbContext context)
    {
        _context = context;
    }

    private Guid GetAuthenticatedTenantId()
    {
        if (HttpContext.Items.TryGetValue("TenantId", out var tenantIdObj) && tenantIdObj is Guid tenantId)
            return tenantId;
        throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");
    }

    [HttpPost]
    public async Task<IActionResult> CreateCredential([FromBody] TenantCredential credential)
    {
        credential.TenantId = GetAuthenticatedTenantId(); // Prevents cross-tenant spoofing
        _context.TenantCredentials.Add(credential);
        await _context.SaveChangesAsync();
        return Ok(credential);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tenantId = GetAuthenticatedTenantId();
        var creds = await _context.TenantCredentials
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync();
        return Ok(creds);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();

        // Strict ownership validation before deletion
        var cred = await _context.TenantCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);

        if (cred == null) return NotFound(new { Error = "Credential not found or access denied." });

        _context.TenantCredentials.Remove(cred);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}