using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public record CreateConnectionRequest(Guid SourceEndpointId, Guid DestinationEndpointId);

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

    private Guid GetAuthenticatedTenantId() =>
        HttpContext.GetTenantId()
        ?? throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tenantId = GetAuthenticatedTenantId();

        var connections = await _context.Connections
            .AsNoTracking()
            .Join(_context.Endpoints, c => c.SourceEndpointId, e => e.Id, (c, e) => new { c, e })
            .Where(x => x.e.TenantId == tenantId)
            .Select(x => x.c)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var ids = connections.Select(c => c.Id).ToList();
        var mappings = await _context.Mappings
            .AsNoTracking()
            .Where(m => ids.Contains(m.ConnectionId))
            .Select(m => new { m.ConnectionId, m.FieldMappings })
            .ToListAsync();

        var ruleCounts = mappings.ToDictionary(m => m.ConnectionId, m => CountRules(m.FieldMappings));

        return Ok(connections.Select(c => new
        {
            c.Id,
            c.SourceEndpointId,
            c.DestinationEndpointId,
            c.Status,
            c.CreatedAt,
            RuleCount = ruleCounts.TryGetValue(c.Id, out var n) ? n : 0
        }));
    }

    [HttpPost]
    public async Task<IActionResult> CreateConnection([FromBody] CreateConnectionRequest request)
    {
        var tenantId = GetAuthenticatedTenantId();

        // Tenant must own BOTH endpoints, and they must be the right types
        var source = await _context.Endpoints.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.SourceEndpointId && e.TenantId == tenantId);
        var dest = await _context.Endpoints.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.DestinationEndpointId && e.TenantId == tenantId);

        if (source == null || dest == null)
            return BadRequest(new { Error = "Source or Destination endpoint not found, or you do not have permission to use them." });

        if (!string.Equals(source.EndpointType, "source", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(dest.EndpointType, "destination", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Error = "Pick a source endpoint and a destination endpoint." });

        // The DB enforces one connection per source endpoint (1:1 relationship)
        var existing = await _context.Connections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SourceEndpointId == request.SourceEndpointId);
        if (existing != null)
        {
            if (existing.DestinationEndpointId == request.DestinationEndpointId)
                return Ok(existing); // idempotent: same pair returns the same connection

            return Conflict(new
            {
                Error = "This source endpoint is already connected to another destination. Delete that connection first.",
                ExistingConnectionId = existing.Id
            });
        }

        var connection = new Connection
        {
            SourceEndpointId = request.SourceEndpointId,
            DestinationEndpointId = request.DestinationEndpointId,
            Status = "MappingRequired",
            CreatedAt = DateTime.UtcNow
        };

        _context.Connections.Add(connection);
        await _context.SaveChangesAsync();
        return Ok(connection);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();

        var connection = await _context.Connections
            .Join(_context.Endpoints, c => c.SourceEndpointId, e => e.Id, (c, e) => new { c, e })
            .Where(x => x.c.Id == id && x.e.TenantId == tenantId)
            .Select(x => x.c)
            .FirstOrDefaultAsync();

        if (connection == null) return NotFound(new { Error = "Connection not found or access denied." });

        _context.Mappings.RemoveRange(_context.Mappings.Where(m => m.ConnectionId == id));
        _context.Connections.Remove(connection);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static int CountRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException) { return 0; }
    }
}
