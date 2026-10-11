using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Infrastructure;
using DomainEndpoint = UniversalMiddleware.Domain.Endpoint;

namespace UniversalMiddleware.API.Controllers;

public record CreateEndpointRequest(string PlatformName, string EndpointType, string? WebhookUrl, string? AuthHeaders);
public record SetSigningSecretRequest(string? SigningSecret);

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class EndpointsController : ControllerBase
{
    private static readonly string[] AllowedTypes = { "source", "destination" };
    private readonly AppDbContext _context;

    public EndpointsController(AppDbContext context)
    {
        _context = context;
    }

    private Guid GetAuthenticatedTenantId() =>
        HttpContext.GetTenantId()
        ?? throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");

    [HttpPost]
    public async Task<IActionResult> CreateEndpoint([FromBody] CreateEndpointRequest request)
    {
        var tenantId = GetAuthenticatedTenantId();

        var type = request.EndpointType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedTypes.Contains(type))
            return BadRequest(new { Error = "endpointType must be 'source' or 'destination'." });

        if (string.IsNullOrWhiteSpace(request.PlatformName))
            return BadRequest(new { Error = "platformName is required." });

        if (type == "destination")
        {
            if (!Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                return BadRequest(new { Error = "Destination endpoints need a valid http(s) webhookUrl." });
        }

        var endpoint = new DomainEndpoint
        {
            TenantId = tenantId, // never trust a client-supplied tenant
            PlatformName = request.PlatformName.Trim(),
            EndpointType = type,
            WebhookUrl = request.WebhookUrl?.Trim() ?? string.Empty,
            AuthHeaders = request.AuthHeaders ?? string.Empty,
            WebhookSecret = type == "source" ? ApiKeys.RandomUrlSafe("whs_") : null
        };

        _context.Endpoints.Add(endpoint);
        await _context.SaveChangesAsync();
        return Ok(ToDto(endpoint));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tenantId = GetAuthenticatedTenantId();
        var endpoints = await _context.Endpoints
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .ToListAsync();

        return Ok(endpoints.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();
        var endpoint = await _context.Endpoints
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);

        if (endpoint == null) return NotFound(new { Error = "Endpoint not found." });
        return Ok(ToDto(endpoint));
    }

    /// <summary>Issues a new secret URL; the old one stops working immediately.</summary>
    [HttpPost("{id:guid}/rotate-webhook-secret")]
    public async Task<IActionResult> RotateWebhookSecret(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();
        var endpoint = await _context.Endpoints.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (endpoint == null) return NotFound(new { Error = "Endpoint not found." });
        if (endpoint.EndpointType != "source")
            return BadRequest(new { Error = "Only source endpoints have an inbound URL." });

        endpoint.WebhookSecret = ApiKeys.RandomUrlSafe("whs_");
        await _context.SaveChangesAsync();
        return Ok(ToDto(endpoint));
    }

    /// <summary>Sets (or clears, with null/empty) the platform signing secret, e.g. Shopify's webhook secret.</summary>
    [HttpPut("{id:guid}/signing-secret")]
    public async Task<IActionResult> SetSigningSecret(Guid id, [FromBody] SetSigningSecretRequest request)
    {
        var tenantId = GetAuthenticatedTenantId();
        var endpoint = await _context.Endpoints.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (endpoint == null) return NotFound(new { Error = "Endpoint not found." });
        if (endpoint.EndpointType != "source")
            return BadRequest(new { Error = "Signing secrets apply to source endpoints only." });

        endpoint.SigningSecret = string.IsNullOrWhiteSpace(request.SigningSecret) ? null : request.SigningSecret.Trim();
        await _context.SaveChangesAsync();
        return Ok(ToDto(endpoint));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();
        var endpoint = await _context.Endpoints.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (endpoint == null) return NotFound(new { Error = "Endpoint not found." });

        // Connections where this is the DESTINATION aren't cascaded by the FK; remove them and their mappings.
        var destConnections = await _context.Connections.Where(c => c.DestinationEndpointId == id).ToListAsync();
        if (destConnections.Count > 0)
        {
            var connIds = destConnections.Select(c => c.Id).ToList();
            _context.Mappings.RemoveRange(_context.Mappings.Where(m => connIds.Contains(m.ConnectionId)));
            _context.Connections.RemoveRange(destConnections);
        }

        _context.Endpoints.Remove(endpoint);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Never return SigningSecret or AuthHeaders; return the inbound path so the UI can build the URL.
    private static object ToDto(DomainEndpoint e) => new
    {
        e.Id,
        e.TenantId,
        e.PlatformName,
        e.EndpointType,
        e.WebhookUrl,
        e.WebhookSecret,
        InboundPath = e.WebhookSecret != null ? $"/api/Webhooks/s/{e.WebhookSecret}" : null,
        HasSigningSecret = !string.IsNullOrEmpty(e.SigningSecret)
    };
}
