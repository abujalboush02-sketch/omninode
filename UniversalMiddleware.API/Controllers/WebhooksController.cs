using Microsoft.AspNetCore.Mvc;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Infrastructure;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class WebhooksController : ControllerBase {
    private readonly AppDbContext _dbContext;
    
    public WebhooksController(AppDbContext dbContext) {
        _dbContext = dbContext;
    }

    [HttpPost("structured/{endpointId}")]
    public async Task<IActionResult> ProcessStructured(Guid endpointId, [FromBody] JsonElement payload) {
        var endpoint = await _dbContext.Endpoints.FirstOrDefaultAsync(e => e.Id == endpointId);
        if (endpoint == null) return NotFound("Endpoint not found.");

        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == endpoint.TenantId);
        if (tenant == null) return NotFound("Tenant not found.");

        if (tenant.TasksUsedThisMonth >= tenant.MonthlyTaskQuota) {
            return StatusCode(429, new { error = "Monthly task quota exceeded. Please upgrade your plan to continue processing webhooks." });
        }

        tenant.TasksUsedThisMonth += 1;

        var rawEvent = new RawEvent {
            EndpointId = endpointId,
            TenantId = endpoint.TenantId,
            Payload = payload.ToString(),
            Status = "Pending",
            RetryCount = 0,
            MaxRetries = 5,
            ReceivedAt = DateTime.UtcNow
        };
        
        _dbContext.RawEvents.Add(rawEvent);
        await _dbContext.SaveChangesAsync();
        
        return Accepted();
    }

    [HttpPost("unstructured/{endpointId}")]
    public async Task<IActionResult> ProcessUnstructured(Guid endpointId, [FromQuery] string userId, [FromBody] JsonElement payload) {
        var endpoint = await _dbContext.Endpoints.FirstOrDefaultAsync(e => e.Id == endpointId);
        if (endpoint == null) return NotFound("Endpoint not found.");

        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == endpoint.TenantId);
        if (tenant == null) return NotFound("Tenant not found.");

        if (tenant.TasksUsedThisMonth >= tenant.MonthlyTaskQuota) {
            return StatusCode(429, new { error = "Monthly task quota exceeded. Please upgrade your plan to continue processing webhooks." });
        }

        tenant.TasksUsedThisMonth += 1;

        var payloadStr = JsonSerializer.Serialize(new { userId, payload });
        
        var rawEvent = new RawEvent {
            EndpointId = endpointId,
            TenantId = endpoint.TenantId,
            Payload = payloadStr,
            Status = "Pending",
            RetryCount = 0,
            MaxRetries = 5,
            ReceivedAt = DateTime.UtcNow
        };
        
        _dbContext.RawEvents.Add(rawEvent);
        await _dbContext.SaveChangesAsync();
        
        return Accepted();
    }
}
