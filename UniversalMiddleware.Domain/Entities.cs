using System;

namespace UniversalMiddleware.Domain;

public class Tenant {
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty;
    public string? BillingProvider { get; set; }
    public string? ExternalBillingReference { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string PlanType { get; set; } = "SaaS";
    public string SubscriptionTier { get; set; } = "Starter";
    public int MonthlyTaskQuota { get; set; } = 1000;
    public int TasksUsedThisMonth { get; set; } = 0;
}

public class TenantCredential {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public Guid EndpointId { get; set; }
    public string AuthType { get; set; } = string.Empty;
}

public class Endpoint {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string EndpointType { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string AuthHeaders { get; set; } = string.Empty;
}

public class SchemaTemplate {
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PlatformName { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = string.Empty;
}

public class Connection {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceEndpointId { get; set; }
    public Guid DestinationEndpointId { get; set; }
    public string Status { get; set; } = "Created";
}

public class Mapping {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public string SourceSchema { get; set; } = string.Empty;
    public string DestinationSchema { get; set; } = string.Empty;
    public string FieldMappings { get; set; } = string.Empty;
}

public class RawEvent {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EndpointId { get; set; }
    public Guid TenantId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending"; 
    
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 5;
    public DateTime? LastAttemptAt { get; set; }
    public string FailureReason { get; set; } = string.Empty;
}

public class IncomingEvent {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RawEventId { get; set; }
    public Guid ConnectionId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = "NeedsMapping";
}

public class ConversationSession {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ExternalUserId { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
}

public class OrderDraft {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = "Incomplete";
}
