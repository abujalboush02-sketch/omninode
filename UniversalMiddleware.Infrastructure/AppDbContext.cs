using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantCredential> TenantCredentials { get; set; }
    public DbSet<Endpoint> Endpoints { get; set; }
    public DbSet<SchemaTemplate> SchemaTemplates { get; set; }
    public DbSet<Connection> Connections { get; set; }
    public DbSet<Mapping> Mappings { get; set; }
    public DbSet<RawEvent> RawEvents { get; set; }
    public DbSet<IncomingEvent> IncomingEvents { get; set; }
    public DbSet<ConversationSession> ConversationSessions { get; set; }
    public DbSet<OrderDraft> OrderDrafts { get; set; }

    // 1. Registered Missing DbSet for EventProcessor Dependency
    public DbSet<FieldMappingRule> FieldMappingRules { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 2. High-Performance Indexes for Background Worker & Auth Lookups
        // This index perfectly matches the EventProcessingWorker's querying pattern
        modelBuilder.Entity<RawEvent>()
            .HasIndex(e => new { e.Status, e.LastAttemptAt, e.ReceivedAt })
            .HasDatabaseName("IX_RawEvent_WorkerQueue");

        // Speeds up the ApiKeyAuthAttribute lookup and prevents key collision
        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.ApiKeyHash)
            .IsUnique();

        // Optimizes the WebhooksController ownership validation query
        modelBuilder.Entity<Endpoint>()
            .HasIndex(e => new { e.TenantId, e.Id });

        // 3. PostgreSQL JSONB Fluent API Enforcements
        modelBuilder.Entity<RawEvent>().Property(e => e.Payload).HasColumnType("jsonb");
        modelBuilder.Entity<SchemaTemplate>().Property(e => e.SchemaJson).HasColumnType("jsonb");
        modelBuilder.Entity<Mapping>().Property(e => e.SourceSchema).HasColumnType("jsonb");
        modelBuilder.Entity<Mapping>().Property(e => e.DestinationSchema).HasColumnType("jsonb");
        modelBuilder.Entity<Mapping>().Property(e => e.FieldMappings).HasColumnType("jsonb");

        // 4. Strict Foreign Key Relationships & Cascade Delete Definitions
        modelBuilder.Entity<Endpoint>()
            .HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent accidental tenant deletion if endpoints exist

        modelBuilder.Entity<RawEvent>()
            .HasOne(e => e.Endpoint)
            .WithMany()
            .HasForeignKey(e => e.EndpointId)
            .OnDelete(DeleteBehavior.Cascade); // Wiping an endpoint clears its queue

        modelBuilder.Entity<FieldMappingRule>()
            .HasOne(e => e.Endpoint)
            .WithMany()
            .HasForeignKey(e => e.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);

        // One-to-One mapping enabling eager loading in EventProcessor.cs
        modelBuilder.Entity<Endpoint>()
            .HasOne(e => e.Connection)
            .WithOne()
            .HasForeignKey<Connection>(c => c.SourceEndpointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}