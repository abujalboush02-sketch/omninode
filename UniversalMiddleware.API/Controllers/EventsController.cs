using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
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

    [HttpGet("logs/{tenantId}")]
    public async Task<IActionResult> GetLogs(Guid tenantId)
    {
        var events = await _context.RawEvents
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.ReceivedAt)
            .Take(50)
            .ToListAsync();
            
        return Ok(events);
    }

    [HttpGet("failed")]
    public async Task<IActionResult> GetFailed()
    {
        var events = await _context.RawEvents
            .Where(e => e.Status == "Failed_ManualReviewRequired")
            .OrderByDescending(e => e.ReceivedAt)
            .ToListAsync();
            
        return Ok(events);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var events = await _context.RawEvents
            .Where(e => e.Status == "Pending")
            .OrderByDescending(e => e.ReceivedAt)
            .ToListAsync();
            
        return Ok(events);
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        var events = await _context.RawEvents
            .OrderByDescending(e => e.ReceivedAt)
            .ToListAsync();
            
        return Ok(events);
    }
}
