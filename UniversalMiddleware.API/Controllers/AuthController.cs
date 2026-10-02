using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Application.Services;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokenService;

    public AuthController(AppDbContext context, IPasswordHasher hasher, ITokenService tokenService)
    {
        _context = context;
        _hasher = hasher;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var emailClean = request.Email.Trim().ToLowerInvariant();

        if (await _context.Users.AnyAsync(u => u.Email == emailClean))
        {
            return BadRequest(new { Error = "An account with this email already exists." });
        }

        // Create user entity
        var user = new User
        {
            Email = emailClean,
            PasswordHash = _hasher.HashPassword(request.Password),
            Role = request.Role
        };

        // If self-registering as a Standalone Tenant
        if (request.Role == UserRole.TenantUser && !request.AgencyId.HasValue)
        {
            var tenant = new Tenant
            {
                Name = request.OrganizationName ?? $"{request.Email}'s Workspace",
                OwnerEmail = emailClean,
                SubscriptionTier = "Starter",
                MonthlyTaskQuota = 150,
                AiTasksQuota = 0,
                TenantType = "Standalone",
                BillingStatus = "Active"
            };

            _context.Tenants.Add(tenant);
            user.Tenant = tenant;
        }
        // If registering an Agency Owner
        else if (request.Role == UserRole.AgencyOwner)
        {
            var agency = new Agency
            {
                Name = request.OrganizationName ?? $"{request.Email} Agency",
                OwnerEmail = emailClean,
                SubscriptionTier = "Agency Bronze",
                MaxSubTenants = 5,
                MonthlyTaskQuota = 25000,
                MonthlyAiQuota = 1000,
                BillingStatus = "PendingPayment" // Activated upon CliQ / Wire approval
            };

            _context.Agencies.Add(agency);
            user.Agency = agency;
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = _tokenService.GenerateToken(user);

        return Ok(new
        {
            Token = token,
            UserId = user.Id,
            user.Email,
            Role = user.Role.ToString(),
            user.AgencyId,
            user.TenantId
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var emailClean = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(u => u.Agency)
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == emailClean);

        if (user == null || !_hasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { Error = "Invalid credentials." });
        }

        if (!user.IsActive)
        {
            return StatusCode(403, new { Error = "Account has been deactivated. Please contact support." });
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var token = _tokenService.GenerateToken(user);

        return Ok(new
        {
            Token = token,
            UserId = user.Id,
            user.Email,
            Role = user.Role.ToString(),
            user.AgencyId,
            user.TenantId
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Agency)
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return NotFound();

        return Ok(new
        {
            user.Id,
            user.Email,
            Role = user.Role.ToString(),
            user.AgencyId,
            AgencyName = user.Agency?.Name,
            user.TenantId,
            TenantName = user.Tenant?.Name,
            user.CreatedAt,
            user.LastLoginAt
        });
    }
}

public record RegisterRequest(
    string Email,
    string Password,
    UserRole Role,
    string? OrganizationName,
    Guid? AgencyId
);

public record LoginRequest(string Email, string Password);