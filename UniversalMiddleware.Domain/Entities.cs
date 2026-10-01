using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversalMiddleware.Domain;

public class FieldMappingRule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EndpointId { get; set; }

    [Required]
    public string SourceKey { get; set; } = string.Empty;

    public string TargetKey { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Math { get; set; } = string.Empty;

    public bool IsRequired { get; set; } = false;

    public virtual Endpoint? Endpoint { get; set; }
}

public class Tenant
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string ApiKeyHash { get; set; } = string.Empty;

    public string? BillingProvider { get; set; }
    public string? ExternalBillingReference { get; set; }

    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;

    public string PlanType { get; set; } = "SaaS";
    public string SubscriptionTier { get; set; } = "Starter";

    public int MonthlyTaskQuota { get; set; } = 1000;

    [ConcurrencyCheck]
    public int TasksUsedThisMonth { get; set; } = 0;
}

public class TenantCredential
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EndpointId { get; set; }

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;

    public virtual Tenant? Tenant { get; set; }
    public virtual Endpoint? Endpoint { get; set; }
}

public class Endpoint
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string PlatformName { get; set; } = string.Empty;
    public string EndpointType { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string AuthHeaders { get; set; } = string.Empty;

    public virtual Tenant? Tenant { get; set; }
    public virtual Connection? Connection { get; set; }
}

public class Connection
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceEndpointId { get; set; }
    public Guid DestinationEndpointId { get; set; }
    public string Status { get; set; } = "Created";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;
    public string? AuthHeaderName { get; set; }
}

public class SchemaTemplate
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PlatformName { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string SchemaJson { get; set; } = string.Empty;
}

public class Mapping
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }

    [Column(TypeName = "jsonb")]
    public string SourceSchema { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string DestinationSchema { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string FieldMappings { get; set; } = string.Empty;
}

public class RawEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EndpointId { get; set; }
    public Guid TenantId { get; set; }

    [Column(TypeName = "jsonb")]
    public string Payload { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 5;
    public DateTime? LastAttemptAt { get; set; }
    public string FailureReason { get; set; } = string.Empty;

    public virtual Endpoint? Endpoint { get; set; }
}

public class IncomingEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RawEventId { get; set; }
    public Guid ConnectionId { get; set; }

    [Column(TypeName = "jsonb")]
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = "NeedsMapping";
}

public class ConversationSession
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ExternalUserId { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
}

public class OrderDraft
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = "Incomplete";
}