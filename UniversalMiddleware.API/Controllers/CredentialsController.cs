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

    [HttpPost]
    public async Task<IActionResult> CreateCredential([FromBody] TenantCredential credential)
    {
        _context.TenantCredentials.Add(credential);
        await _context.SaveChangesAsync();
        return Ok(credential);
    }

    [HttpGet("tenant/{tenantId}")]
    public async Task<IActionResult> GetByTenant(Guid tenantId)
    {
        var creds = await _context.TenantCredentials.Where(c => c.TenantId == tenantId).ToListAsync();
        return Ok(creds);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var cred = await _context.TenantCredentials.FindAsync(id);
        if (cred == null) return NotFound();
        
        _context.TenantCredentials.Remove(cred);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
