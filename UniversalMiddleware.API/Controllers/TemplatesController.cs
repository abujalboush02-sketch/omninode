using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

/// <summary>
/// Built-in platform templates (seeded by DataSeeder). Ad-hoc "Custom_*" rows created by
/// /api/schemas/discover-custom are excluded so tenants never see each other's samples.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class TemplatesController : ControllerBase
{
    private readonly AppDbContext _context;

    public TemplatesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var templates = await _context.SchemaTemplates
            .AsNoTracking()
            .Where(t => !t.PlatformName.StartsWith("Custom_"))
            .OrderBy(t => t.PlatformName)
            .ToListAsync();

        return Ok(templates.Select(t => new
        {
            t.Id,
            Name = t.PlatformName,
            Platform = t.PlatformName,
            SampleJson = t.SchemaJson,
            Fields = Flatten(t.SchemaJson)
        }));
    }

    private static List<string> Flatten(string json)
    {
        var fields = new List<string>();
        try
        {
            if (JsonNode.Parse(json) is JsonObject obj) Walk(obj, "", fields);
        }
        catch (JsonException) { /* bad seed row: return no fields */ }
        return fields;
    }

    private static void Walk(JsonObject node, string prefix, List<string> fields)
    {
        foreach (var (key, value) in node)
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            switch (value)
            {
                case JsonObject child:
                    Walk(child, path, fields);
                    break;
                case JsonArray arr when arr.Count > 0 && arr[0] is JsonObject first:
                    Walk(first, path + "[]", fields);
                    break;
                default:
                    fields.Add(path);
                    break;
            }
        }
    }
}
