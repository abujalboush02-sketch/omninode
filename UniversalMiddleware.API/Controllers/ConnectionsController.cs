using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class ConnectionsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ConnectionsController(AppDbContext context)
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
    public async Task<IActionResult> CreateConnection([FromBody] Connection connection)
    {
        var tenantId = GetAuthenticatedTenantId();

        // STRICT SECURITY: Verify the authenticated tenant actually owns BOTH endpoints
        var ownsSource = await _context.Endpoints.AnyAsync(e => e.Id == connection.SourceEndpointId && e.TenantId == tenantId);
        var ownsDest = await _context.Endpoints.AnyAsync(e => e.Id == connection.DestinationEndpointId && e.TenantId == tenantId);

        if (!ownsSource || !ownsDest)
        {
            return BadRequest(new { Error = "Source or Destination endpoint not found, or you do not have permission to use them." });
        }

        _context.Connections.Add(connection);
        await _context.SaveChangesAsync();

        return Ok(connection);
    }
}