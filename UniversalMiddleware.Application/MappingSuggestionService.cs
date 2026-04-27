using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public class MappingSuggestionService {
    public List<FieldMappingRule> GetSuggestedMappings(string sourceSchemaJson, string destinationSchemaJson) {
        var suggestions = new List<FieldMappingRule>();
        
        try {
            var sourceObj = JsonNode.Parse(sourceSchemaJson) as JsonObject;
            var destObj = JsonNode.Parse(destinationSchemaJson) as JsonObject;
            
            if (sourceObj == null || destObj == null) return suggestions;

            foreach (var srcKvp in sourceObj) {
                var srcKey = srcKvp.Key.ToLower().Replace("_", "").Replace("-", "");
                
                foreach (var destKvp in destObj) {
                    var destKey = destKvp.Key.ToLower().Replace("_", "").Replace("-", "");
                    
                    if (srcKey == destKey || srcKey.Contains(destKey) || destKey.Contains(srcKey)) {
                        suggestions.Add(new FieldMappingRule {
                            SourceKey = srcKvp.Key,
                            DestinationKey = destKvp.Key,
                            TransformationType = "Direct"
                        });
                        break;
                    }
                }
            }
        } catch { }
        
        return suggestions;
    }
}
