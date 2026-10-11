using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public record RegisterDeviceRequest(string Token, string Platform);

/// <summary>
/// Stores push tokens (FCM). Sending pushes needs Firebase credentials on the server;
/// that's a separate step (see APPLY.md). Registering now means no app update is needed later.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private static readonly string[] Platforms = { "android", "ios", "web" };
    private readonly AppDbContext _context;

    public DevicesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        var platform = request.Platform?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 4096 || !Platforms.Contains(platform))
            return BadRequest(new { Error = "token and platform (android|ios|web) are required." });

        var token = request.Token.Trim();
        var existing = await _context.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
        if (existing != null)
        {
            existing.UserId = userId; // a device can change hands after logout/login
            existing.Platform = platform;
            existing.LastSeenAt = DateTime.UtcNow;
        }
        else
        {
            _context.DeviceTokens.Add(new DeviceToken { UserId = userId, Token = token, Platform = platform });
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{token}")]
    public async Task<IActionResult> Unregister(string token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        var device = await _context.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token && d.UserId == userId);
        if (device != null)
        {
            _context.DeviceTokens.Remove(device);
            await _context.SaveChangesAsync();
        }
        return NoContent();
    }
}
