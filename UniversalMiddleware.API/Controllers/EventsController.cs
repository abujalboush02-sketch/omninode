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

    private Guid GetAuthenticatedTenantId()
    {
        if (HttpContext.Items.TryGetValue("TenantId", out var tenantIdObj) && tenantIdObj is Guid tenantId)
            return tenantId;
        throw new UnauthorizedAccessException("Tenant ID is missing from the authenticated context.");
    }

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
}