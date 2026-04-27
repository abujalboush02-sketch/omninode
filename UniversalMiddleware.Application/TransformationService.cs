using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UniversalMiddleware.Application;

public class TransformationService : ITransformationService {
    public string TransformPayload(string sourcePayload, Domain.Mapping mapping) {
        try {
            var sourceJson = JsonNode.Parse(sourcePayload) as JsonObject;
            var mappings = JsonNode.Parse(mapping.FieldMappings) as JsonObject;
            var targetJson = new JsonObject();

            if (sourceJson == null || mappings == null) return "{}";

            foreach (var kvp in mappings) {
                var sourceKey = kvp.Key;
                var rule = kvp.Value as JsonObject;
                
                if (rule == null || !sourceJson.ContainsKey(sourceKey)) continue;

                var targetKey = rule["target"]?.ToString() ?? sourceKey;
                var type = rule["type"]?.ToString();
                var math = rule["math"]?.ToString();

                var rawValue = sourceJson[sourceKey];
                
                if (type == "number" && rawValue != null) {
                    if (double.TryParse(rawValue.ToString(), out double numValue)) {
                        if (!string.IsNullOrEmpty(math) && math.StartsWith("/")) {
                            if (double.TryParse(math.Substring(1), out double divisor) && divisor != 0) {
                                numValue /= divisor;
                            }
                        }
                        targetJson[targetKey] = numValue;
                    }
                } else {
                    targetJson[targetKey] = rawValue?.ToString();
                }
            }
            
            return targetJson.ToJsonString();
        } catch {
            return "{}";
        }
    }
}
