using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Infrastructure;

public class AppDbContext : DbContext {
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
}
