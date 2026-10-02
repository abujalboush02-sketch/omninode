using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[Authorize(Roles = "SuperAdmin")]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;

    public AdminController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Global operational telemetry and billing metrics.
    /// </summary>
    [HttpGet("telemetry")]
    public async Task<IActionResult> GetTelemetry()
    {
        var totalAgencies = await _context.Agencies.CountAsync();
        var totalTenants = await _context.Tenants.CountAsync();
        var totalUsers = await _context.Users.CountAsync();

        var totalStandardTasksUsed = await _context.Tenants.SumAsync(t => t.TasksUsedThisMonth);
        var totalAiTasksUsed = await _context.Tenants.SumAsync(t => t.AiTasksUsedThisMonth);

        var pendingEventsCount = await _context.RawEvents.CountAsync(e => e.Status == "Pending" || e.Status == "Processing");
        var failedEventsCount = await _context.RawEvents.CountAsync(e => e.Status == "Failed_ManualReviewRequired");

        var unpaidInvoicesTotal = await _context.PaymentInvoices
            .Where(p => p.Status == "Unpaid")
            .SumAsync(p => p.Amount);

        return Ok(new
        {
            GlobalCounts = new
            {
                Agencies = totalAgencies,
                Tenants = totalTenants,
                Users = totalUsers
            },
            TaskUsageThisMonth = new
            {
                StandardTasks = totalStandardTasksUsed,
                AiTasks = totalAiTasksUsed
            },
            QueueHealth = new
            {
                ActivePendingQueue = pendingEventsCount,
                DeadLetterQueueFailures = failedEventsCount
            },
            Financials = new
            {
                UnpaidInvoicesSum = unpaidInvoicesTotal
            }
        });
    }

    /// <summary>
    /// Returns the global ledger of all agencies with sub-tenant statistics.
    /// </summary>
    [HttpGet("agencies")]
    public async Task<IActionResult> GetAllAgencies()
    {
        var agencies = await _context.Agencies
            .AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.OwnerEmail,
                a.SubscriptionTier,
                a.BillingStatus,
                a.NextRenewalDate,
                a.MaxSubTenants,
                CurrentSubTenants = a.Tenants.Count,
                a.MonthlyTaskQuota,
                a.TasksUsedThisMonth,
                a.MonthlyAiQuota,
                a.AiTasksUsedThisMonth,
                a.CreatedAt
            })
            .ToListAsync();

        return Ok(agencies);
    }

    /// <summary>
    /// Returns the Dead-Letter Queue (all events requiring manual intervention).
    /// </summary>
    [HttpGet("dead-letter-queue")]
    public async Task<IActionResult> GetDeadLetterQueue([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var events = await _context.RawEvents
            .AsNoTracking()
            .Where(e => e.Status == "Failed_ManualReviewRequired")
            .OrderByDescending(e => e.LastAttemptAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.TenantId,
                e.EndpointId,
                e.ReceivedAt,
                e.LastAttemptAt,
                e.RetryCount,
                e.FailureReason,
                e.Payload
            })
            .ToListAsync();

        return Ok(events);
    }

    /// <summary>
    /// Bulk re-queues all failed events (or events for a specific endpoint) to run through the pipeline again.
    /// </summary>
    [HttpPost("reprocess-failed")]
    public async Task<IActionResult> ReprocessFailedEvents([FromQuery] Guid? endpointId = null)
    {
        var query = _context.RawEvents
            .Where(e => e.Status == "Failed_ManualReviewRequired");

        if (endpointId.HasValue)
        {
            query = query.Where(e => e.EndpointId == endpointId.Value);
        }

        var resetCount = await query.ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, "Pending")
            .SetProperty(e => e.RetryCount, 0)
            .SetProperty(e => e.LastAttemptAt, (DateTime?)null)
            .SetProperty(e => e.FailureReason, (string?)null));

        return Ok(new
        {
            Message = $"Successfully re-queued {resetCount} failed event(s) for immediate processing."
        });
    }
}