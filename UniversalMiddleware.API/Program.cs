using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using UniversalMiddleware.Infrastructure;
using UniversalMiddleware.Application;
using UniversalMiddleware.Application.Services;
using UniversalMiddleware.API;

var builder = WebApplication.CreateBuilder(args);

// 1. Production-Ready Logging (Enrichment + Global Capture)
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 2. API Documentation Setup (Branded for OmniNode consumers)
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OmniNode Middleware API",
        Version = "v1",
        Description = "Multi-tenant routing API bridging e-commerce platforms with external CRMs."
    });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key for authentication. Example: `X-Api-Key: YOUR_API_KEY`",
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "ApiKey"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});

// 3. Resilient Database Connection (Retry on Transient Failures)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)));

// 4. LLM Service Setup
builder.Services.Configure<LlamaSettings>(builder.Configuration.GetSection("LlamaSettings"));
builder.Services.AddHttpClient<ILlamaAgentService, LlamaAgentService>(client =>
{
    var baseUrl = builder.Configuration["LlamaSettings:BaseUrl"];
    client.BaseAddress = new Uri(string.IsNullOrEmpty(baseUrl) ? "https://api.groq.com/openai/v1" : baseUrl);
});

// 5. Dependency Injection
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddScoped<ISchemaDiscoveryService, SchemaDiscoveryService>();
builder.Services.AddScoped<ITransformationService, TransformationService>();
builder.Services.AddScoped<IConnectionWizardService, ConnectionWizardService>();
builder.Services.AddScoped<IEventProcessor, EventProcessor>();
builder.Services.AddScoped<IGenericHttpOutputAdapter, GenericHttpOutputAdapter>();
builder.Services.AddScoped<SchemaBuilderService>();
builder.Services.AddScoped<MappingValidationService>();
builder.Services.AddScoped<MappingSuggestionService>();
builder.Services.AddHttpClient("OutboundCrmClient");

builder.Services.AddHostedService<EventProcessingWorker>();
builder.Services.AddHostedService<BillingLifecycleWorker>();

// JWT Authentication Setup
var jwtKey = builder.Configuration["Jwt:Key"] ?? "OmniNode_Fallback_Super_Secret_Jwt_Encryption_Key_2026";
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
});

// 6. APILayer & Docker Monitoring (Health Checks)
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

// 7. Nginx Reverse Proxy Headers (Preserves real client IP)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Clear restrictions to ensure Docker bridge networks don't block the headers
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// 8. CORS Strategy (Required for Agency Dashboard Frontends)
builder.Services.AddCors(options =>
{
    options.AddPolicy("OmniNodeCorsPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure Nginx reverse proxy headers before any logging or routing
app.UseForwardedHeaders();

// Attach Serilog to capture HTTP requests
app.UseSerilogRequestLogging();

// 9. Safe Asynchronous Database Initialization
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        // Applies migrations without dropping tables (Requires EF Core Migrations configured)
        await context.Database.MigrateAsync();
        await DataSeeder.SeedTemplatesAsync(context);
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "An error occurred while migrating or seeding the database.");
    }
}

// 10. Swagger exposed unconditionally for API consumers
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "OmniNode API V1");
    // Maps Swagger to domain.com/docs to avoid conflict with the root domain API mapping
    c.RoutePrefix = "docs";
});

app.UseCors("OmniNodeCorsPolicy");
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Map the health check endpoint
app.MapHealthChecks("/health");
app.MapControllers();

try
{
    Log.Information("Starting OmniNode API host...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}