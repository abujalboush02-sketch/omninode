using System;
using System.Collections.Generic;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public class MappingValidationService {
    public bool ValidateRules(List<FieldMappingRule> rules) {
        foreach (var rule in rules) {
            if (rule.TransformationType == "Math" && string.IsNullOrEmpty(rule.TransformationRule)) {
                return false; // Math requires a rule
            }
        }
        return true;
    }
}
