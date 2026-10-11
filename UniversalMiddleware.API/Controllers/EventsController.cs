using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class EventsController : ControllerBase
{
    private const string FailedStatus = "Failed_ManualReviewRequired";
    private readonly AppDbContext _context;

    public EventsController(AppDbContext context)
    {
        _context = context;
    }

    private Guid GetAuthenticatedTenantId() =>
        HttpContext.GetTenantId()
        ?? throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");

    [HttpGet("logs")]
    public Task<IActionResult> GetLogs() => Query(null, 50);

    [HttpGet("failed")]
    public Task<IActionResult> GetFailed() => Query(FailedStatus, 100);

    [HttpGet("pending")]
    public Task<IActionResult> GetPending() => Query("Pending", 100);

    [HttpGet("all")]
    public Task<IActionResult> GetAll() => Query(null, 200); // hard limit

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();
        var count = await RequeueFailed(_context.RawEvents.Where(e => e.Id == id && e.TenantId == tenantId));

        return count == 0
            ? NotFound(new { Error = "Failed event not found." })
            : Ok(new { Message = "Event re-queued." });
    }

    /// <summary>Re-queues every failed event of the tenant (optionally one endpoint).</summary>
    [HttpPost("retry-failed")]
    public async Task<IActionResult> RetryFailed([FromQuery] Guid? endpointId = null)
    {
        var tenantId = GetAuthenticatedTenantId();
        var query = _context.RawEvents.Where(e => e.TenantId == tenantId);
        if (endpointId.HasValue) query = query.Where(e => e.EndpointId == endpointId.Value);

        var count = await RequeueFailed(query);
        return Ok(new { Requeued = count });
    }

    private async Task<IActionResult> Query(string? status, int take)
    {
        var tenantId = GetAuthenticatedTenantId();
        var query = _context.RawEvents.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (status != null) query = query.Where(e => e.Status == status);

        var events = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Take(take)
            .Select(e => new
            {
                e.Id,
                e.EndpointId,
                e.TenantId,
                e.Payload,
                e.ReceivedAt,
                e.Status,
                e.RetryCount,
                e.MaxRetries,
                e.LastAttemptAt,
                e.FailureReason
            })
            .ToListAsync();

        return Ok(events);
    }

    private static Task<int> RequeueFailed(IQueryable<UniversalMiddleware.Domain.RawEvent> query) =>
        query.Where(e => e.Status == FailedStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, "Pending")
                .SetProperty(e => e.RetryCount, 0)
                .SetProperty(e => e.LastAttemptAt, (DateTime?)null)
                .SetProperty(e => e.FailureReason, string.Empty));
}
