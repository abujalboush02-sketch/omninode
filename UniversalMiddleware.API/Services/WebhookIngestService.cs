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
/// Standalone tenant: tenant quota only. Agency client: client allocation AND the agency pool (both with 110% grace).
/// </summary>
public class WebhookIngestService
{
    private static readonly string[] InactiveTenant = { "Suspended", "Paused" };
    private static readonly string[] InactiveAgency = { "Suspended", "PendingPayment" };

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

                if (InactiveTenant.Contains(tenant.BillingStatus))
                    return Error(402, tenant.BillingStatus == "Paused"
                        ? "This workspace is paused by its agency. Webhook processing is paused."
                        : "Tenant account is suspended due to unpaid invoices. Webhook processing is paused.", tenant, null);

                Agency? agency = null;
                if (tenant.AgencyId is Guid agencyId)
                {
                    agency = await _db.Agencies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agencyId);
                    if (agency == null)
                        return new NotFoundObjectResult(new { Error = "Agency record not found." });

                    if (InactiveAgency.Contains(agency.BillingStatus))
                        return Error(402, agency.BillingStatus == "PendingPayment"
                            ? "The agency plan isn't active yet. Webhooks start once the first payment is confirmed."
                            : "The agency account is suspended due to unpaid invoices. Webhook processing is paused.", tenant, agency);
                }

                // 1. Client / standalone tenant quota (atomic, 110% grace)
                int rows;
                if (isAi)
                {
                    rows = await _db.Tenants
                        .Where(t => t.Id == tenantId &&
                                    t.TasksUsedThisMonth * Plans.GraceDenominator < t.MonthlyTaskQuota * Plans.GraceNumerator &&
                                    t.AiTasksUsedThisMonth * Plans.GraceDenominator < t.AiTasksQuota * Plans.GraceNumerator)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.TasksUsedThisMonth, t => t.TasksUsedThisMonth + 1)
                            .SetProperty(t => t.AiTasksUsedThisMonth, t => t.AiTasksUsedThisMonth + 1));
                }
                else
                {
                    rows = await _db.Tenants
                        .Where(t => t.Id == tenantId &&
                                    t.TasksUsedThisMonth * Plans.GraceDenominator < t.MonthlyTaskQuota * Plans.GraceNumerator)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.TasksUsedThisMonth, t => t.TasksUsedThisMonth + 1));
                }

                if (rows == 0)
                {
                    await transaction.RollbackAsync();
                    return Error(429, agency != null
                        ? "This client's monthly allocation is used up. The agency can raise it from its portal."
                        : isAi ? "Monthly AI or task quota exceeded. Please upgrade your plan."
                               : "Monthly task quota exceeded. Please upgrade your plan.", tenant, agency);
                }

                // 2. Agency pool (atomic, 110% grace). Rolls back the client increment if the pool is empty.
                if (agency != null)
                {
                    int poolRows;
                    if (isAi)
                    {
                        poolRows = await _db.Agencies
                            .Where(a => a.Id == agency.Id &&
                                        a.TasksUsedThisMonth * Plans.GraceDenominator < a.MonthlyTaskQuota * Plans.GraceNumerator &&
                                        a.AiTasksUsedThisMonth * Plans.GraceDenominator < a.MonthlyAiQuota * Plans.GraceNumerator)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(a => a.TasksUsedThisMonth, a => a.TasksUsedThisMonth + 1)
                                .SetProperty(a => a.AiTasksUsedThisMonth, a => a.AiTasksUsedThisMonth + 1));
                    }
                    else
                    {
                        poolRows = await _db.Agencies
                            .Where(a => a.Id == agency.Id &&
                                        a.TasksUsedThisMonth * Plans.GraceDenominator < a.MonthlyTaskQuota * Plans.GraceNumerator)
                            .ExecuteUpdateAsync(s => s.SetProperty(a => a.TasksUsedThisMonth, a => a.TasksUsedThisMonth + 1));
                    }

                    if (poolRows == 0)
                    {
                        await transaction.RollbackAsync();
                        return Error(429, "The agency's monthly task pool is used up. The agency needs to upgrade its plan.", tenant, agency);
                    }

                    if (agency.TasksUsedThisMonth + 1 == agency.MonthlyTaskQuota)
                        TelegramAlert.Fire($"⚠️ Agency pool reached (100%)\nAgency: {agency.Name}\nPlan: {agency.SubscriptionTier}\nLimit: {agency.MonthlyTaskQuota} tasks (grace to 110%).");
                }

                // Alert once, on the exact request that reaches 100% of the client's own quota
                if (tenant.TasksUsedThisMonth + 1 == tenant.MonthlyTaskQuota)
                {
                    TelegramAlert.Fire(agency != null
                        ? $"ℹ️ Client allocation reached (100%)\nAgency: {agency.Name}\nClient: {tenant.Name}\nAllocation: {tenant.MonthlyTaskQuota}"
                        : $"⚠️ Quota reached (100%)\nTenant: {tenant.Name}\nPlan: {tenant.SubscriptionTier}\nLimit: {tenant.MonthlyTaskQuota} tasks (grace to 110%).\nAction: Call them for an upgrade.");
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

    private static ObjectResult Error(int status, string message, Tenant tenant, Agency? agency) =>
        new(new
        {
            Error = message,
            tenant.BillingStatus,
            tenant.TasksUsedThisMonth,
            tenant.MonthlyTaskQuota,
            tenant.AiTasksUsedThisMonth,
            tenant.AiTasksQuota,
            AgencyBillingStatus = agency?.BillingStatus,
            AgencyTasksUsedThisMonth = agency?.TasksUsedThisMonth,
            AgencyMonthlyTaskQuota = agency?.MonthlyTaskQuota
        })
        { StatusCode = status };
}
