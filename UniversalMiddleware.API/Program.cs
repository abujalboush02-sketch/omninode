using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Infrastructure;
using UniversalMiddleware.Application;
using Serilog;
using UniversalMiddleware.API;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models; // Models namespace is back!

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// === EXACT SWAGGER CONFIG FROM WOOFOOD ===
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Universal Middleware API", Version = "v1" });

    // Configure API Key authentication for Swagger UI
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
            new string[] { }
        }
    });
});

// RESTORED POSTGRESQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<LlamaSettings>(builder.Configuration.GetSection("LlamaSettings"));
builder.Services.AddHttpClient<ILlamaAgentService, LlamaAgentService>(client => 
{
    var baseUrl = builder.Configuration["LlamaSettings:BaseUrl"];
    client.BaseAddress = new Uri(string.IsNullOrEmpty(baseUrl) ? "https://api.groq.com/openai/v1" : baseUrl);
});

// RESTORED SERVICES
builder.Services.AddScoped<IWebhookProcessor, WebhookProcessor>();
builder.Services.AddScoped<ISchemaDiscoveryService, SchemaDiscoveryService>();
builder.Services.AddScoped<ITransformationService, TransformationService>();
builder.Services.AddScoped<IConnectionWizardService, ConnectionWizardService>();
builder.Services.AddScoped<IEventProcessor, EventProcessor>();
builder.Services.AddScoped<IGenericHttpOutputAdapter, GenericHttpOutputAdapter>();
builder.Services.AddScoped<SchemaBuilderService>();
builder.Services.AddScoped<MappingValidationService>();
builder.Services.AddScoped<MappingSuggestionService>();

builder.Services.AddHostedService<EventProcessingWorker>();

var app = builder.Build();

// RESTORED SEEDER WITH AUTO-CREATE
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
    // This forces Postgres to create the database and tables based on your models!
    context.Database.EnsureCreated(); 
    
    // Then seed the data
    DataSeeder.SeedTemplatesAsync(context).Wait();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();