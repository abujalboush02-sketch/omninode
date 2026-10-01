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
public class EndpointsController : ControllerBase
{
    private readonly AppDbContext _context;

    public EndpointsController(AppDbContext context)
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
    public async Task<IActionResult> CreateEndpoint([FromBody] UniversalMiddleware.Domain.Endpoint endpoint)
    {
        // 1. Hard-override the payload's TenantId to prevent spoofing
        endpoint.TenantId = GetAuthenticatedTenantId();

        _context.Endpoints.Add(endpoint);
        await _context.SaveChangesAsync();
        return Ok(endpoint);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tenantId = GetAuthenticatedTenantId();
        var endpoints = await _context.Endpoints
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .ToListAsync();

        return Ok(endpoints);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();

        // 2. Strict ID and Ownership Validation
        var endpoint = await _context.Endpoints
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);

        if (endpoint == null) return NotFound(new { Error = "Endpoint not found." });

        return Ok(endpoint);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();

        var endpoint = await _context.Endpoints
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);

        if (endpoint == null) return NotFound(new { Error = "Endpoint not found." });

        _context.Endpoints.Remove(endpoint);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}