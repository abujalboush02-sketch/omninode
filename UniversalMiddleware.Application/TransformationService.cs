using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public class TransformationService : ITransformationService
{
    private readonly ILogger<TransformationService> _logger;

    public TransformationService(ILogger<TransformationService> logger)
    {
        _logger = logger;
    }

    // 1. Interface implementation required by the upgraded EventProcessor.cs
    public Task<string> TransformAsync(string sourcePayload, IEnumerable<FieldMappingRule> rules)
    {
        if (string.IsNullOrWhiteSpace(sourcePayload))
            throw new ArgumentException("Source payload cannot be empty.");

        var sourceJson = JsonNode.Parse(sourcePayload) as JsonObject;
        var targetJson = new JsonObject();

        if (sourceJson == null)
            throw new FormatException("Source payload is not a valid JSON object.");

        foreach (var rule in rules)
        {
            var rawValue = ExtractNestedValue(sourceJson, rule.SourceKey);

            if (rawValue == null)
            {
                _logger.LogDebug("Source key '{SourceKey}' not found in payload. Skipping.", rule.SourceKey);
                continue;
            }

            var targetKey = string.IsNullOrWhiteSpace(rule.TargetKey) ? rule.SourceKey : rule.TargetKey;

            if (string.Equals(rule.Type, "number", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(rawValue.ToString(), out double numValue))
                {
                    numValue = ApplyMathOperation(numValue, rule.Math);
                    targetJson[targetKey] = numValue;
                }
                else
                {
                    _logger.LogWarning("Failed to parse '{Value}' as number for field '{Key}'.", rawValue, rule.SourceKey);
                }
            }
            else
            {
                targetJson[targetKey] = rawValue.DeepClone();
            }
        }

        return Task.FromResult(targetJson.ToJsonString());
    }

    // 2. Upgraded Legacy Method (Resolves critical silent failure risks)
    public string TransformPayload(string sourcePayload, Mapping mapping)
    {
        try
        {
            var sourceJson = JsonNode.Parse(sourcePayload) as JsonObject;
            var mappings = JsonNode.Parse(mapping.FieldMappings) as JsonObject;
            var targetJson = new JsonObject();

            if (sourceJson == null || mappings == null)
                throw new FormatException("Invalid JSON in source payload or mapping definition.");

            foreach (var kvp in mappings)
            {
                var sourceKey = kvp.Key;
                var rule = kvp.Value as JsonObject;

                if (rule == null) continue;

                var rawValue = ExtractNestedValue(sourceJson, sourceKey);
                if (rawValue == null) continue;

                var targetKey = rule["target"]?.ToString() ?? sourceKey;
                var type = rule["type"]?.ToString();
                var math = rule["math"]?.ToString();

                if (type == "number")
                {
                    if (double.TryParse(rawValue.ToString(), out double numValue))
                    {
                        numValue = ApplyMathOperation(numValue, math);
                        targetJson[targetKey] = numValue;
                    }
                }
                else
                {
                    targetJson[targetKey] = rawValue.DeepClone();
                }
            }

            return targetJson.ToJsonString();
        }
        catch (Exception ex)
        {
            // CRITICAL FIX: Bubble up exceptions so the EventProcessingWorker triggers a retry
            _logger.LogError(ex, "Payload transformation failed.");
            throw new InvalidOperationException("Failed to transform payload.", ex);
        }
    }

    // 3. Dot-Notation Extraction (Resolves deeply nested e-commerce fields)
    private JsonNode ExtractNestedValue(JsonObject root, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var parts = path.Split('.');
        JsonNode current = root;

        foreach (var part in parts)
        {
            if (current is JsonObject obj && obj.ContainsKey(part))
            {
                current = obj[part];
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    // 4. Expanded Math Engine
    private double ApplyMathOperation(double value, string math)
    {
        if (string.IsNullOrWhiteSpace(math) || math.Length < 2) return value;

        char op = math[0];
        if (double.TryParse(math.Substring(1), out double operand))
        {
            return op switch
            {
                '/' => operand != 0 ? value / operand : value,
                '*' => value * operand,
                '+' => value + operand,
                '-' => value - operand,
                _ => value
            };
        }
        return value;
    }
}