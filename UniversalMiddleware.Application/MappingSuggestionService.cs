using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public class MappingSuggestionService
{
    private readonly ILogger<MappingSuggestionService> _logger;

    public MappingSuggestionService(ILogger<MappingSuggestionService> logger)
    {
        _logger = logger;
    }

    public List<FieldMappingRule> GetSuggestedMappings(string sourceSchemaJson, string destinationSchemaJson)
    {
        var suggestions = new List<FieldMappingRule>();

        if (string.IsNullOrWhiteSpace(sourceSchemaJson) || string.IsNullOrWhiteSpace(destinationSchemaJson))
        {
            _logger.LogWarning("Schema mapping suggestion aborted: Source or destination JSON is empty.");
            return suggestions;
        }

        try
        {
            var sourceObj = JsonNode.Parse(sourceSchemaJson) as JsonObject;
            var destObj = JsonNode.Parse(destinationSchemaJson) as JsonObject;

            if (sourceObj == null || destObj == null)
            {
                _logger.LogWarning("Schema mapping suggestion aborted: Invalid JSON objects provided.");
                return suggestions;
            }

            var sourceFields = FlattenJson(sourceObj);
            var destFields = FlattenJson(destObj);

            foreach (var src in sourceFields)
            {
                var normalizedSrc = NormalizeKey(src.Key);

                foreach (var dest in destFields)
                {
                    var normalizedDest = NormalizeKey(dest.Key);

                    if (IsSafeMatch(normalizedSrc, normalizedDest))
                    {
                        suggestions.Add(new FieldMappingRule
                        {
                            SourceKey = src.Key,
                            TargetKey = dest.Key, // Aligned with Domain Model
                            Type = "string"       // Aligned with Domain Model
                        });

                        destFields.Remove(dest.Key);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate mapping suggestions due to an unexpected parsing error.");
            throw new InvalidOperationException("Failed to analyze schema schemas.", ex);
        }

        return suggestions;
    }

    private Dictionary<string, string> FlattenJson(JsonObject node, string prefix = "")
    {
        var result = new Dictionary<string, string>();

        foreach (var kvp in node)
        {
            var path = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

            if (kvp.Value is JsonObject childObj)
            {
                var nested = FlattenJson(childObj, path);
                foreach (var nestedKvp in nested)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else if (kvp.Value is JsonArray)
            {
                result[path] = "array";
            }
            else
            {
                result[path] = "value";
            }
        }
        return result;
    }

    private string NormalizeKey(string key)
    {
        var leafNode = key.Split('.').Last();
        return leafNode.ToLowerInvariant().Replace("_", "").Replace("-", "");
    }

    private bool IsSafeMatch(string src, string dest)
    {
        if (src == dest) return true;
        if (src.Length < 4 || dest.Length < 4) return false;
        return src.EndsWith(dest) || dest.EndsWith(src);
    }
}