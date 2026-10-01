using System;
using System.Collections.Generic;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public class MappingValidationService
{
    public bool ValidateRules(List<FieldMappingRule> rules)
    {
        foreach (var rule in rules)
        {
            // Updated to match the new Domain property names (Type and Math)
            if (string.Equals(rule.Type, "math", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(rule.Math))
            {
                return false;
            }
        }
        return true;
    }
}