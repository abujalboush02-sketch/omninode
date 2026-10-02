using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Infrastructure;
using UniversalMiddleware.Domain;
using UniversalMiddleware.API.Attributes;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth] // Guarantees HttpContext.Items["TenantId"] is populated for valid tenants
public class WebhooksController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public WebhooksController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private Guid GetAuthenticatedTenantId()
    {
        if (HttpContext.Items.TryGetValue("TenantId", out var tenantIdObj) && tenantIdObj is Guid tenantId)
        {
            return tenantId;
        }
        throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");
    }

    [HttpPost("structured/{endpointId:guid}")]
    public async Task<IActionResult> ProcessStructured(Guid endpointId, [FromBody] JsonElement payload)
    {
        var authTenantId = GetAuthenticatedTenantId();

        // 1. Cross-Tenant Data Injection Prevention
        var endpointExists = await _dbContext.Endpoints
            .AsNoTracking()
            .AnyAsync(e => e.Id == endpointId && e.TenantId == authTenantId);

        if (!endpointExists)
            return NotFound(new { Error = "Endpoint not found or you do not have permission to access it." });

        // 2. Resilience: Wrap explicit transaction in Execution Strategy (Required by EnableRetryOnFailure)
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        var result = await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // Check Billing & Account Lifecycle Status
                var tenant = await _dbContext.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == authTenantId);

                if (tenant == null)
                    return NotFound(new { Error = "Tenant record not found." });

                if (tenant.BillingStatus == "Suspended")
                {
                    return StatusCode(402, new { Error = "Tenant account is suspended due to unpaid invoices. Webhook processing is paused." });
                }

                // 3. Thread-Safe Quota Increment (Avoids Race Conditions)
                var rowsAffected = await _dbContext.Tenants
                    .Where(t => t.Id == authTenantId && t.TasksUsedThisMonth < t.MonthlyTaskQuota)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.TasksUsedThisMonth, t => t.TasksUsedThisMonth + 1));

                if (rowsAffected == 0)
                {
                    return StatusCode(429, new { Error = "Monthly task quota exceeded. Please upgrade your plan." });
                }

                // 4. Memory-Optimized Payload Extraction
                var rawEvent = new RawEvent
                {
                    EndpointId = endpointId,
                    TenantId = authTenantId,
                    Payload = payload.GetRawText(), // Eliminates generic ToString() overhead
                    Status = "Pending",
                    RetryCount = 0,
                    MaxRetries = 5,
                    ReceivedAt = DateTime.UtcNow
                };

                _dbContext.RawEvents.Add(rawEvent);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return Accepted(new { Message = "Webhook queued successfully for processing." });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        return result;
    }

    [HttpPost("unstructured/{endpointId:guid}")]
    public async Task<IActionResult> ProcessUnstructured(Guid endpointId, [FromQuery] string userId, [FromBody] JsonElement payload)
    {
        var authTenantId = GetAuthenticatedTenantId();

        var endpointExists = await _dbContext.Endpoints
            .AsNoTracking()
            .AnyAsync(e => e.Id == endpointId && e.TenantId == authTenantId);

        if (!endpointExists)
            return NotFound(new { Error = "Endpoint not found or you do not have permission to access it." });

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        var result = await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // Check Billing & Account Lifecycle Status
                var tenant = await _dbContext.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == authTenantId);

                if (tenant == null)
                    return NotFound(new { Error = "Tenant record not found." });

                if (tenant.BillingStatus == "Suspended")
                {
                    return StatusCode(402, new { Error = "Tenant account is suspended due to unpaid invoices. Webhook processing is paused." });
                }

                // Meter both standard tasks and AI unstructured extraction tasks atomically
                var rowsAffected = await _dbContext.Tenants
                    .Where(t => t.Id == authTenantId &&
                                t.TasksUsedThisMonth < t.MonthlyTaskQuota &&
                                t.AiTasksUsedThisMonth < t.AiTasksQuota)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.TasksUsedThisMonth, t => t.TasksUsedThisMonth + 1)
                        .SetProperty(t => t.AiTasksUsedThisMonth, t => t.AiTasksUsedThisMonth + 1));

                if (rowsAffected == 0)
                {
                    return StatusCode(429, new { Error = "Monthly AI or task quota exceeded. Please upgrade your plan." });
                }

                // Safely wrap the unstructured payload alongside the userId identifier
                var wrappedPayload = JsonSerializer.Serialize(new { userId, payload });

                var rawEvent = new RawEvent
                {
                    EndpointId = endpointId,
                    TenantId = authTenantId,
                    Payload = wrappedPayload,
                    Status = "Pending",
                    RetryCount = 0,
                    MaxRetries = 5,
                    ReceivedAt = DateTime.UtcNow
                };

                _dbContext.RawEvents.Add(rawEvent);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return Accepted(new { Message = "Unstructured webhook queued successfully for parsing." });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        return result;
    }
}