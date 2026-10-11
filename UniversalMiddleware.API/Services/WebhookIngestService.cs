using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Services;

/// <summary>
/// Single place that meters quota and queues a RawEvent.
/// Used by WebhooksController (API key / JWT) and PublicWebhooksController (secret URL).
/// </summary>
public class WebhookIngestService
{
    private readonly AppDbContext _db;

    public WebhookIngestService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> IngestAsync(Guid tenantId, Guid endpointId, string payloadJson, bool isAi)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
                if (tenant == null)
                    return new NotFoundObjectResult(new { Error = "Tenant record not found." });

                if (tenant.BillingStatus == "Suspended")
                    return Error(402, "Tenant account is suspended due to unpaid invoices. Webhook processing is paused.", tenant);

                // Atomic quota increment with a 110% grace buffer (integer math so EF can translate it)
                int rowsAffected;
                if (isAi)
                {
                    rowsAffected = await _db.Tenants
                        .Where(t => t.Id == tenantId &&
                                    t.TasksUsedThisMonth * Plans.GraceDenominator < t.MonthlyTaskQuota * Plans.GraceNumerator &&
                                    t.AiTasksUsedThisMonth * Plans.GraceDenominator < t.AiTasksQuota * Plans.GraceNumerator)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.TasksUsedThisMonth, t => t.TasksUsedThisMonth + 1)
                            .SetProperty(t => t.AiTasksUsedThisMonth, t => t.AiTasksUsedThisMonth + 1));
                }
                else
                {
                    rowsAffected = await _db.Tenants
                        .Where(t => t.Id == tenantId &&
                                    t.TasksUsedThisMonth * Plans.GraceDenominator < t.MonthlyTaskQuota * Plans.GraceNumerator)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.TasksUsedThisMonth, t => t.TasksUsedThisMonth + 1));
                }

                if (rowsAffected == 0)
                {
                    await transaction.RollbackAsync();
                    return Error(429, isAi
                        ? "Monthly AI or task quota exceeded. Please upgrade your plan."
                        : "Monthly task quota exceeded. Please upgrade your plan.", tenant);
                }

                // Alert once, on the exact request that reaches 100% (not on every rejected webhook)
                if (tenant.TasksUsedThisMonth + 1 == tenant.MonthlyTaskQuota)
                {
                    TelegramAlert.Fire($"⚠️ Quota reached (100%)\nTenant: {tenant.Name}\nPlan: {tenant.SubscriptionTier}\nLimit: {tenant.MonthlyTaskQuota} tasks (grace to 110%).\nAction: Call them for an upgrade.");
                }

                _db.RawEvents.Add(new RawEvent
                {
                    EndpointId = endpointId,
                    TenantId = tenantId,
                    Payload = payloadJson,
                    Status = "Pending",
                    RetryCount = 0,
                    MaxRetries = 5,
                    ReceivedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return new AcceptedResult(location: (string?)null, value: new
                {
                    Message = isAi
                        ? "Unstructured webhook queued successfully for parsing."
                        : "Webhook queued successfully for processing."
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private static ObjectResult Error(int status, string message, Tenant tenant) =>
        new(new
        {
            Error = message,
            tenant.BillingStatus,
            tenant.TasksUsedThisMonth,
            tenant.MonthlyTaskQuota,
            tenant.AiTasksUsedThisMonth,
            tenant.AiTasksQuota
        })
        { StatusCode = status };
}
