using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;
using UniversalMiddleware.Application;

namespace UniversalMiddleware.API.Controllers;

public class ConfigureMappingRequest {
    public Guid ConnectionId { get; set; }
    public List<FieldMappingRule> Rules { get; set; } = new List<FieldMappingRule>();
}

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class MappingsController : ControllerBase {
    private readonly AppDbContext _context;
    private readonly MappingValidationService _validationService;
    private readonly MappingSuggestionService _suggestionService;

    public MappingsController(AppDbContext context, MappingValidationService validationService, MappingSuggestionService suggestionService) {
        _context = context;
        _validationService = validationService;
        _suggestionService = suggestionService;
    }

    [HttpPost("configure")]
    public async Task<IActionResult> ConfigureMapping([FromBody] ConfigureMappingRequest request) {
        if (!_validationService.ValidateRules(request.Rules)) {
            return BadRequest("Invalid mapping rules detected.");
        }

        var mapping = _context.Mappings.FirstOrDefault(m => m.ConnectionId == request.ConnectionId);
        if (mapping == null) {
            mapping = new Mapping { ConnectionId = request.ConnectionId };
            _context.Mappings.Add(mapping);
        }

        mapping.FieldMappings = JsonSerializer.Serialize(request.Rules);
        
        var connection = _context.Connections.FirstOrDefault(c => c.Id == request.ConnectionId);
        if (connection != null && connection.Status == "MappingRequired") {
            connection.Status = "Active";
        }
        
        await _context.SaveChangesAsync();
        
        return Ok(mapping);
    }

    [HttpGet("suggest/{connectionId}")]
    public IActionResult SuggestMappings(Guid connectionId) {
        var mapping = _context.Mappings.FirstOrDefault(m => m.ConnectionId == connectionId);
        if (mapping == null) return NotFound();

        var suggestions = _suggestionService.GetSuggestedMappings(mapping.SourceSchema, mapping.DestinationSchema);
        return Ok(suggestions);
    }
}
