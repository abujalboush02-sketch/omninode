using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _context;

    public EventsController(AppDbContext context)
    {
        _context = context;
    }

    private Guid GetAuthenticatedTenantId() =>
    HttpContext.GetTenantId()
    ?? throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs()
    {
        var tenantId = GetAuthenticatedTenantId();
        var events = await _context.RawEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.ReceivedAt)
            .Take(50)
            .ToListAsync();

        return Ok(events);
    }

    [HttpGet("failed")]
    public async Task<IActionResult> GetFailed()
    {
        var tenantId = GetAuthenticatedTenantId();
        var events = await _context.RawEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Status == "Failed_ManualReviewRequired")
            .OrderByDescending(e => e.ReceivedAt)
            .Take(100)
            .ToListAsync();

        return Ok(events);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var tenantId = GetAuthenticatedTenantId();
        var events = await _context.RawEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Status == "Pending")
            .OrderByDescending(e => e.ReceivedAt)
            .Take(100)
            .ToListAsync();

        return Ok(events);
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        var tenantId = GetAuthenticatedTenantId();
        var events = await _context.RawEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.ReceivedAt)
            .Take(200) // Hard limit prevents server crash
            .ToListAsync();

        return Ok(events);
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id)
    {
        var tenantId = GetAuthenticatedTenantId();

        var count = await _context.RawEvents
            .Where(e => e.Id == id && e.TenantId == tenantId && e.Status == "Failed_ManualReviewRequired")
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, "Pending")
                .SetProperty(e => e.RetryCount, 0)
                .SetProperty(e => e.LastAttemptAt, (DateTime?)null)
                .SetProperty(e => e.FailureReason, (string?)null));

        return count == 0
            ? NotFound(new { Error = "Failed event not found." })
            : Ok(new { Message = "Event re-queued." });
    }
}