using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.API.Services;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

/// <summary>
/// Authenticated intake: X-Api-Key header, ?key= query parameter, or tenant JWT.
/// For senders that can't authenticate at all (Shopify), use PublicWebhooksController (/api/Webhooks/s/{secret}).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class WebhooksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly WebhookIngestService _ingest;

    public WebhooksController(AppDbContext db, WebhookIngestService ingest)
    {
        _db = db;
        _ingest = ingest;
    }

    private Guid GetAuthenticatedTenantId() =>
        HttpContext.GetTenantId()
        ?? throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");

    [HttpPost("structured/{endpointId:guid}")]
    public async Task<IActionResult> ProcessStructured(Guid endpointId, [FromBody] JsonElement payload)
    {
        var tenantId = GetAuthenticatedTenantId();
        if (!await OwnsEndpoint(endpointId, tenantId))
            return NotFound(new { Error = "Endpoint not found or you do not have permission to access it." });

        return await _ingest.IngestAsync(tenantId, endpointId, payload.GetRawText(), isAi: false);
    }

    [HttpPost("unstructured/{endpointId:guid}")]
    public async Task<IActionResult> ProcessUnstructured(Guid endpointId, [FromQuery] string userId, [FromBody] JsonElement payload)
    {
        var tenantId = GetAuthenticatedTenantId();
        if (!await OwnsEndpoint(endpointId, tenantId))
            return NotFound(new { Error = "Endpoint not found or you do not have permission to access it." });

        var wrapped = JsonSerializer.Serialize(new { userId, payload });
        return await _ingest.IngestAsync(tenantId, endpointId, wrapped, isAi: true);
    }

    private Task<bool> OwnsEndpoint(Guid endpointId, Guid tenantId) =>
        _db.Endpoints.AsNoTracking().AnyAsync(e => e.Id == endpointId && e.TenantId == tenantId);
}
