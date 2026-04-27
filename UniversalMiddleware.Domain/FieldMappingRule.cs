namespace UniversalMiddleware.Domain;

public class FieldMappingRule {
    public string SourceKey { get; set; } = string.Empty;
    public string DestinationKey { get; set; } = string.Empty;
    public string TransformationType { get; set; } = string.Empty; // Math, TypeCast, Static, Direct
    public string TransformationRule { get; set; } = string.Empty; // e.g. "/100", "ToDecimal"
    public bool IsRequired { get; set; } = false;
}
