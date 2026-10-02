using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;
using UniversalMiddleware.Application;

namespace UniversalMiddleware.API.Controllers;

public class ConfigureMappingRequest
{
    public Guid ConnectionId { get; set; }
    public List<FieldMappingRule> Rules { get; set; } = new List<FieldMappingRule>();
}

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class MappingsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly MappingValidationService _validationService;
    private readonly MappingSuggestionService _suggestionService;

    public MappingsController(AppDbContext context, MappingValidationService validationService, MappingSuggestionService suggestionService)
    {
        _context = context;
        _validationService = validationService;
        _suggestionService = suggestionService;
    }

    private Guid GetAuthenticatedTenantId()
    {
        if (HttpContext.Items.TryGetValue("TenantId", out var tenantIdObj) && tenantIdObj is Guid tenantId)
            return tenantId;
        throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");
    }

    [HttpPost("configure")]
    public async Task<IActionResult> ConfigureMapping([FromBody] ConfigureMappingRequest request)
    {
        var tenantId = GetAuthenticatedTenantId();

        if (!_validationService.ValidateRules(request.Rules))
        {
            return BadRequest(new { Error = "Invalid mapping rules detected." });
        }

        // Deep ownership verification: Connection -> Endpoint -> Tenant
        var connectionExists = await _context.Connections
            .Join(_context.Endpoints, c => c.SourceEndpointId, e => e.Id, (c, e) => new { c, e })
            .AnyAsync(x => x.c.Id == request.ConnectionId && x.e.TenantId == tenantId);

        if (!connectionExists)
        {
            return NotFound(new { Error = "Connection not found or access denied." });
        }

        var mapping = await _context.Mappings.FirstOrDefaultAsync(m => m.ConnectionId == request.ConnectionId);
        if (mapping == null)
        {
            // FIX: Explicitly initialize required jsonb columns with valid empty JSON
            mapping = new Mapping
            {
                ConnectionId = request.ConnectionId,
                SourceSchema = "{}",
                DestinationSchema = "{}"
            };
            _context.Mappings.Add(mapping);
        }

        mapping.FieldMappings = JsonSerializer.Serialize(request.Rules);

        var connection = await _context.Connections.FirstOrDefaultAsync(c => c.Id == request.ConnectionId);
        if (connection != null && connection.Status == "MappingRequired")
        {
            connection.Status = "Active";
        }

        await _context.SaveChangesAsync();

        return Ok(mapping);
    }

    [HttpGet("suggest/{connectionId:guid}")]
    public async Task<IActionResult> SuggestMappings(Guid connectionId)
    {
        var tenantId = GetAuthenticatedTenantId();

        var connectionExists = await _context.Connections
            .Join(_context.Endpoints, c => c.SourceEndpointId, e => e.Id, (c, e) => new { c, e })
            .AnyAsync(x => x.c.Id == connectionId && x.e.TenantId == tenantId);

        if (!connectionExists)
        {
            return NotFound(new { Error = "Connection not found or access denied." });
        }

        var mapping = await _context.Mappings.AsNoTracking().FirstOrDefaultAsync(m => m.ConnectionId == connectionId);
        if (mapping == null) return NotFound(new { Error = "Mapping not found." });

        var suggestions = _suggestionService.GetSuggestedMappings(mapping.SourceSchema, mapping.DestinationSchema);
        return Ok(suggestions);
    }
}