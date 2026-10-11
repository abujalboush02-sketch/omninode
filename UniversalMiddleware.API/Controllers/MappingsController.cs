using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Application;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public class ConfigureMappingRequest
{
    public Guid ConnectionId { get; set; }
    public List<FieldMappingRule> Rules { get; set; } = new();

    // Optional sample payloads. When sent, they're stored so /suggest has real schemas to compare.
    public string? SourceSchema { get; set; }
    public string? DestinationSchema { get; set; }
}

public record SuggestRequest(string SourceJson, string DestinationJson);

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class MappingsController : ControllerBase
{
    // Reading only: case-insensitive, so it handles both stored PascalCase and any camelCase rows
    private static readonly JsonSerializerOptions RuleJson = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _context;
    private readonly MappingValidationService _validationService;
    private readonly MappingSuggestionService _suggestionService;

    public MappingsController(AppDbContext context, MappingValidationService validationService, MappingSuggestionService suggestionService)
    {
        _context = context;
        _validationService = validationService;
        _suggestionService = suggestionService;
    }

    private Guid GetAuthenticatedTenantId() =>
        HttpContext.GetTenantId()
        ?? throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");

    private Task<bool> OwnsConnection(Guid connectionId, Guid tenantId) =>
        _context.Connections
            .Join(_context.Endpoints, c => c.SourceEndpointId, e => e.Id, (c, e) => new { c, e })
            .AnyAsync(x => x.c.Id == connectionId && x.e.TenantId == tenantId);

    /// <summary>Reload the saved rules for a connection.</summary>
    [HttpGet("{connectionId:guid}")]
    public async Task<IActionResult> GetMapping(Guid connectionId)
    {
        var tenantId = GetAuthenticatedTenantId();
        if (!await OwnsConnection(connectionId, tenantId))
            return NotFound(new { Error = "Connection not found or access denied." });

        var mapping = await _context.Mappings.AsNoTracking().FirstOrDefaultAsync(m => m.ConnectionId == connectionId);
        if (mapping == null) return NotFound(new { Error = "Mapping not found." });

        List<FieldMappingRule> rules;
        try
        {
            rules = string.IsNullOrWhiteSpace(mapping.FieldMappings)
                ? new List<FieldMappingRule>()
                : JsonSerializer.Deserialize<List<FieldMappingRule>>(mapping.FieldMappings, RuleJson) ?? new List<FieldMappingRule>();
        }
        catch (JsonException)
        {
            rules = new List<FieldMappingRule>();
        }

        return Ok(new
        {
            mapping.ConnectionId,
            Rules = rules.Select(r => new { r.EndpointId, r.SourceKey, r.TargetKey, r.Type, r.Math, r.IsRequired }),
            SourceSchema = NullIfEmptyObject(mapping.SourceSchema),
            DestinationSchema = NullIfEmptyObject(mapping.DestinationSchema),
            mapping.UpdatedAt
        });
    }

    [HttpPost("configure")]
    public async Task<IActionResult> ConfigureMapping([FromBody] ConfigureMappingRequest request)
    {
        var tenantId = GetAuthenticatedTenantId();

        if (!_validationService.ValidateRules(request.Rules))
            return BadRequest(new { Error = "Invalid mapping rules detected." });

        if (!await OwnsConnection(request.ConnectionId, tenantId))
            return NotFound(new { Error = "Connection not found or access denied." });

        if (request.SourceSchema != null && !IsJsonObject(request.SourceSchema))
            return BadRequest(new { Error = "sourceSchema must be a JSON object." });
        if (request.DestinationSchema != null && !IsJsonObject(request.DestinationSchema))
            return BadRequest(new { Error = "destinationSchema must be a JSON object." });

        var mapping = await _context.Mappings.FirstOrDefaultAsync(m => m.ConnectionId == request.ConnectionId);
        if (mapping == null)
        {
            mapping = new Mapping { ConnectionId = request.ConnectionId, SourceSchema = "{}", DestinationSchema = "{}" };
            _context.Mappings.Add(mapping);
        }

        if (request.SourceSchema != null) mapping.SourceSchema = request.SourceSchema;
        if (request.DestinationSchema != null) mapping.DestinationSchema = request.DestinationSchema;

        // Store with DEFAULT serializer options (PascalCase) - EventProcessor reads this column.
        mapping.FieldMappings = JsonSerializer.Serialize(
            request.Rules.Select(r => new FieldMappingRule
            {
                Id = r.Id,
                EndpointId = r.EndpointId,
                SourceKey = r.SourceKey,
                TargetKey = r.TargetKey,
                Type = r.Type,
                Math = r.Math,
                IsRequired = r.IsRequired
            }).ToList());
        mapping.UpdatedAt = DateTime.UtcNow;

        var connection = await _context.Connections.FirstOrDefaultAsync(c => c.Id == request.ConnectionId);
        if (connection != null && connection.Status != "Active")
            connection.Status = "Active"; // covers legacy "Created" and "MappingRequired"

        await _context.SaveChangesAsync();

        return Ok(new { mapping.ConnectionId, RuleCount = request.Rules.Count, mapping.UpdatedAt, Status = connection?.Status });
    }

    /// <summary>Suggestions from the schemas saved on the connection's mapping.</summary>
    [HttpGet("suggest/{connectionId:guid}")]
    public async Task<IActionResult> SuggestMappings(Guid connectionId)
    {
        var tenantId = GetAuthenticatedTenantId();
        if (!await OwnsConnection(connectionId, tenantId))
            return NotFound(new { Error = "Connection not found or access denied." });

        var mapping = await _context.Mappings.AsNoTracking().FirstOrDefaultAsync(m => m.ConnectionId == connectionId);
        if (mapping == null) return NotFound(new { Error = "Mapping not found." });

        return Suggest(mapping.SourceSchema, mapping.DestinationSchema);
    }

    /// <summary>Stateless suggestions from two sample payloads (use before anything is saved).</summary>
    [HttpPost("suggest")]
    public IActionResult SuggestFromSamples([FromBody] SuggestRequest request)
    {
        if (!IsJsonObject(request.SourceJson) || !IsJsonObject(request.DestinationJson))
            return BadRequest(new { Error = "sourceJson and destinationJson must both be JSON objects." });

        return Suggest(request.SourceJson, request.DestinationJson);
    }

    private IActionResult Suggest(string source, string destination)
    {
        try
        {
            var suggestions = _suggestionService.GetSuggestedMappings(source, destination);
            return Ok(suggestions.Select(s => new { s.SourceKey, s.TargetKey, s.Type }));
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { Error = "Could not analyze the provided schemas." });
        }
    }

    private static bool IsJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try { return JsonNode.Parse(json) is JsonObject; }
        catch (JsonException) { return false; }
    }

    private static string? NullIfEmptyObject(string? json) =>
        string.IsNullOrWhiteSpace(json) || json.Trim() == "{}" ? null : json;
}
