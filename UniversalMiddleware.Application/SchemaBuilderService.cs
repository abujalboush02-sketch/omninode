using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UniversalMiddleware.Application;

public class SchemaBuilderService {
    public string BuildSchemaFromFields(List<string> fieldNames) {
        var jsonObject = new JsonObject();
        foreach (var field in fieldNames) {
            var lowerField = field.ToLower();
            if (lowerField.Contains("price") || lowerField.Contains("total") || lowerField.Contains("amount")) {
                jsonObject[field] = 0.00;
            } else if (lowerField.Contains("date")) {
                jsonObject[field] = DateTime.UtcNow.ToString("O");
            } else {
                jsonObject[field] = "sample";
            }
        }
        return jsonObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
