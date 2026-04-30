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

    [HttpPost]
    public async Task<IActionResult> CreateEndpoint([FromBody] UniversalMiddleware.Domain.Endpoint endpoint)
    {
        _context.Endpoints.Add(endpoint);
        await _context.SaveChangesAsync();
        return Ok(endpoint);
    }

    [HttpGet("tenant/{tenantId}")]
    public async Task<IActionResult> GetByTenant(Guid tenantId)
    {
        var endpoints = await _context.Endpoints.Where(e => e.TenantId == tenantId).ToListAsync();
        return Ok(endpoints);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var endpoint = await _context.Endpoints.FindAsync(id);
        if (endpoint == null) return NotFound();
        return Ok(endpoint);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var endpoint = await _context.Endpoints.FindAsync(id);
        if (endpoint == null) return NotFound();
        
        _context.Endpoints.Remove(endpoint);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
