using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalMiddleware.Application;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public class DiscoverCustomRequest {
    public string RawJson { get; set; } = string.Empty;
    public List<string> FieldList { get; set; } = new List<string>();
    public Guid TenantId { get; set; }
}

[ApiController]
[Route("api/schemas")]
[ApiKeyAuth]
public class DiscoveryController : ControllerBase {
    private readonly AppDbContext _context;
    private readonly SchemaBuilderService _schemaBuilder;

    public DiscoveryController(AppDbContext context, SchemaBuilderService schemaBuilder) {
        _context = context;
        _schemaBuilder = schemaBuilder;
    }

    [HttpPost("discover-custom")]
    public async Task<IActionResult> DiscoverCustom([FromBody] DiscoverCustomRequest request) {
        string jsonSchema = "";
        
        if (request.FieldList != null && request.FieldList.Count > 0) {
            jsonSchema = _schemaBuilder.BuildSchemaFromFields(request.FieldList);
        } else if (!string.IsNullOrEmpty(request.RawJson)) {
            jsonSchema = request.RawJson;
        } else {
            return BadRequest("Provide FieldList or RawJson");
        }

        var template = new SchemaTemplate {
            PlatformName = "Custom_" + Guid.NewGuid().ToString().Substring(0, 8),
            SchemaJson = jsonSchema
        };
        
        _context.SchemaTemplates.Add(template);
        await _context.SaveChangesAsync();
        
        return Ok(template);
    }
}
