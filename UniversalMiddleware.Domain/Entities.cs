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

    // Add these properties inside your Tenant class:
    public Guid? AgencyId { get; set; }
    public Agency? Agency { get; set; }

    public string TenantType { get; set; } = "Standalone"; // "Standalone" or "AgencySubTenant"
    public string BillingStatus { get; set; } = "Active"; // Active, PendingPayment, GracePeriod, Suspended
    public int AiTasksQuota { get; set; } = 100;
    public int AiTasksUsedThisMonth { get; set; } = 0;
    public DateTime NextRenewalDate { get; set; } = DateTime.UtcNow.AddDays(30);
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

public class Agency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string SubscriptionTier { get; set; } = "Bronze"; // Bronze, Silver, Gold, Platinum
    public int MaxSubTenants { get; set; } = 5;
    public int MonthlyTaskQuota { get; set; } = 25000;
    public int MonthlyAiQuota { get; set; } = 1000;
    public int TasksUsedThisMonth { get; set; } = 0;
    public int AiTasksUsedThisMonth { get; set; } = 0;
    public string BillingStatus { get; set; } = "Active"; // Active, PendingPayment, GracePeriod, Suspended
    public DateTime NextRenewalDate { get; set; } = DateTime.UtcNow.AddDays(30);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    // Optional organizational scope
    public Guid? AgencyId { get; set; }
    public Agency? Agency { get; set; }

    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

public class PaymentInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountType { get; set; } = "Tenant"; // "Agency" or "Tenant"
    public Guid AccountId { get; set; } // Points to AgencyId or TenantId
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "JOD"; // JOD, USD
    public string PaymentMethod { get; set; } = "CliQ"; // CliQ, BankWire, Cash
    public string ReferenceCode { get; set; } = string.Empty; // e.g. "INV-2026-X" for matching CliQ/Wire memo
    public string Status { get; set; } = "Unpaid"; // Unpaid, Confirmed, Rejected
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public Guid? ConfirmedByAdminId { get; set; }
}

public class UsageLedger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public Guid? AgencyId { get; set; }
    public string BillingCycle { get; set; } = string.Empty; // Format: "YYYY-MM"
    public int StandardTasksUsed { get; set; }
    public int AiTasksUsed { get; set; }
    public decimal OverageIncurred { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}


public enum UserRole
{
    SuperAdmin,
    AgencyOwner,
    TenantUser
}