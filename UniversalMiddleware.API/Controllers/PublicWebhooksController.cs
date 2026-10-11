using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Services;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

/// <summary>
/// Header-less intake for platforms like Shopify that can't send X-Api-Key.
/// The unguessable secret in the URL authenticates the call:
///   POST /api/Webhooks/s/{webhookSecret}
/// If the endpoint has a SigningSecret, X-Shopify-Hmac-Sha256 is also verified.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/Webhooks/s")]
public class PublicWebhooksController : ControllerBase
{
    private const int MaxBodyBytes = 1_048_576; // 1 MB
    private readonly AppDbContext _db;
    private readonly WebhookIngestService _ingest;

    public PublicWebhooksController(AppDbContext db, WebhookIngestService ingest)
    {
        _db = db;
        _ingest = ingest;
    }

    [HttpPost("{secret}")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 20)
            return NotFound(new { Error = "Unknown webhook URL." });

        var endpoint = await _db.Endpoints
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.WebhookSecret == secret);

        // Same 404 for every miss so the route can't be probed for valid secrets
        if (endpoint == null || !string.Equals(endpoint.EndpointType, "source", StringComparison.OrdinalIgnoreCase))
            return NotFound(new { Error = "Unknown webhook URL." });

        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        var bodyBytes = ms.ToArray();

        if (!string.IsNullOrEmpty(endpoint.SigningSecret))
        {
            var provided = Request.Headers["X-Shopify-Hmac-Sha256"].ToString();
            if (string.IsNullOrEmpty(provided) || !VerifyHmac(endpoint.SigningSecret, bodyBytes, provided))
                return Unauthorized(new { Error = "Invalid webhook signature." });
        }

        string json;
        try
        {
            using var doc = JsonDocument.Parse(bodyBytes);
            json = doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return BadRequest(new { Error = "Body must be valid JSON." });
        }

        return await _ingest.IngestAsync(endpoint.TenantId, endpoint.Id, json, isAi: false);
    }

    private static bool VerifyHmac(string signingSecret, byte[] body, string providedBase64)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var expected = hmac.ComputeHash(body);

        byte[] provided;
        try { provided = Convert.FromBase64String(providedBase64.Trim()); }
        catch (FormatException) { return false; }

        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
