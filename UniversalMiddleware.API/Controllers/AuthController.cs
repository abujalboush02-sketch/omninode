using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.API.Services;
using UniversalMiddleware.Application.Services;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const int MinPasswordLength = 8;

    private readonly AppDbContext _context;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokenService;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext context, IPasswordHasher hasher, ITokenService tokenService,
        IEmailSender email, IConfiguration config)
    {
        _context = context;
        _hasher = hasher;
        _tokenService = tokenService;
        _email = email;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            return BadRequest(new { Error = "A valid email is required." });
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < MinPasswordLength)
            return BadRequest(new { Error = $"Password must be at least {MinPasswordLength} characters." });

        // Only tenant or agency-owner self-signup. Never SuperAdmin, never unknown enum values.
        if (request.Role != UserRole.TenantUser && request.Role != UserRole.AgencyOwner)
            return BadRequest(new { Error = "Invalid role." });

        // Agency members are provisioned by their agency, not by claiming an AgencyId at signup.
        if (request.AgencyId.HasValue)
            return BadRequest(new { Error = "Agency members are invited by their agency." });

        var emailClean = request.Email.Trim().ToLowerInvariant();
        if (await _context.Users.AnyAsync(u => u.Email == emailClean))
            return BadRequest(new { Error = "An account with this email already exists." });

        var user = new User
        {
            Email = emailClean,
            PasswordHash = _hasher.HashPassword(request.Password),
            Role = request.Role
        };

        string? rawApiKey = null;

        if (request.Role == UserRole.TenantUser)
        {
            var (raw, hash) = ApiKeys.Generate();
            rawApiKey = raw;

            var free = Plans.Find("free")!;
            var tenant = new Tenant
            {
                Name = string.IsNullOrWhiteSpace(request.OrganizationName) ? $"{emailClean}'s Workspace" : request.OrganizationName.Trim(),
                OwnerName = string.IsNullOrWhiteSpace(request.OrganizationName) ? emailClean : request.OrganizationName.Trim(),
                OwnerEmail = emailClean,
                PlanType = "Standalone",
                TenantType = "Standalone",
                BillingStatus = "Trial",
                NextRenewalDate = DateTime.UtcNow.AddDays(Plans.TrialDays), // trial end
                ApiKeyHash = hash,
                ApiKeyHint = ApiKeys.Hint(raw)
            };
            Plans.Apply(tenant, free); // Free Trial: 14 days, 150 tasks, 20 AI

            _context.Tenants.Add(tenant);
            user.Tenant = tenant;
        }
        else
        {
            var agency = new Agency
            {
                Name = string.IsNullOrWhiteSpace(request.OrganizationName) ? $"{emailClean} Agency" : request.OrganizationName.Trim(),
                OwnerEmail = emailClean,
                // 14-day trial with small limits; buying a plan replaces them.
                BillingStatus = "Trial",
                NextRenewalDate = DateTime.UtcNow.AddDays(Plans.TrialDays)
            };
            AgencyPlans.Apply(agency, AgencyPlans.Find("agency_trial")!);

            _context.Agencies.Add(agency);
            user.Agency = agency;
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        TelegramAlert.Fire($"🚀 New Signup!\nEmail: {emailClean}\nRole: {request.Role}");

        return Ok(new
        {
            Token = _tokenService.GenerateToken(user),
            UserId = user.Id,
            user.Email,
            Role = user.Role.ToString(),
            user.AgencyId,
            user.TenantId,
            ApiKey = rawApiKey // shown once; the frontend must store it
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var emailClean = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(u => u.Agency)
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == emailClean);

        if (user == null || !_hasher.VerifyPassword(request.Password ?? string.Empty, user.PasswordHash))
            return Unauthorized(new { Error = "Invalid credentials." });

        if (!user.IsActive)
            return StatusCode(403, new { Error = "Account has been deactivated. Please contact support." });

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            Token = _tokenService.GenerateToken(user),
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
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Agency)
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

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

    /// <summary>Updates the organisation (tenant or agency) name and owner name.</summary>
    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        var user = await _context.Users.Include(u => u.Tenant).Include(u => u.Agency).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return Unauthorized();

        var org = request.OrganizationName?.Trim();
        var owner = request.OwnerName?.Trim();
        if (org != null && (org.Length < 2 || org.Length > 255))
            return BadRequest(new { Error = "Organization name must be 2-255 characters." });
        if (owner != null && owner.Length > 255)
            return BadRequest(new { Error = "Owner name is too long." });

        if (user.Agency != null)
        {
            if (!string.IsNullOrEmpty(org)) user.Agency.Name = org;
        }
        else if (user.Tenant != null)
        {
            if (!string.IsNullOrEmpty(org)) user.Tenant.Name = org;
            if (!string.IsNullOrEmpty(owner)) user.Tenant.OwnerName = owner;
        }

        await _context.SaveChangesAsync();
        return Ok(new
        {
            OrganizationName = user.Agency?.Name ?? user.Tenant?.Name,
            OwnerName = user.Tenant?.OwnerName
        });
    }

    /// <summary>Changes the login email (password required). Returns a fresh token with the new email.</summary>
    [Authorize]
    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        var newEmail = (request.NewEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (!newEmail.Contains('@') || newEmail.Length > 255)
            return BadRequest(new { Error = "A valid email is required." });

        var user = await _context.Users.Include(u => u.Tenant).Include(u => u.Agency).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return Unauthorized();

        if (!_hasher.VerifyPassword(request.Password ?? string.Empty, user.PasswordHash))
            return BadRequest(new { Error = "Password is incorrect." });

        if (newEmail == user.Email) return BadRequest(new { Error = "That's already your email." });
        if (await _context.Users.AnyAsync(u => u.Email == newEmail))
            return BadRequest(new { Error = "An account with this email already exists." });

        var oldEmail = user.Email;
        user.Email = newEmail;
        if (user.Tenant != null && user.Tenant.OwnerEmail == oldEmail) user.Tenant.OwnerEmail = newEmail;
        if (user.Agency != null && user.Agency.OwnerEmail == oldEmail) user.Agency.OwnerEmail = newEmail;

        await _context.SaveChangesAsync();

        await _email.SendAsync(oldEmail, "Your OmniNode email was changed",
            $"<p>The login email for your OmniNode account was changed to {WebUtility.HtmlEncode(newEmail)}.</p>" +
            "<p>If this wasn't you, contact support immediately.</p>");

        return Ok(new { Token = _tokenService.GenerateToken(user), user.Email });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < MinPasswordLength)
            return BadRequest(new { Error = $"New password must be at least {MinPasswordLength} characters." });

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        if (!_hasher.VerifyPassword(request.CurrentPassword ?? string.Empty, user.PasswordHash))
            return BadRequest(new { Error = "Current password is incorrect." });

        user.PasswordHash = _hasher.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Always 202, so it never reveals whether an email is registered.</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var emailClean = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == emailClean && u.IsActive);

        if (user != null)
        {
            // Invalidate older unused tokens for this user
            var now = DateTime.UtcNow;
            var old = await _context.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > now)
                .ToListAsync();
            foreach (var t in old) t.UsedAt = now;

            var rawToken = ApiKeys.RandomUrlSafe("rst_", 32);
            _context.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = ApiKeys.Hash(rawToken),
                ExpiresAt = now.AddHours(1)
            });
            await _context.SaveChangesAsync();

            var frontend = (_config["App:FrontendUrl"] ?? "https://omni.mohammad-abujalboush.com").TrimEnd('/');
            var link = $"{frontend}/#/reset-password?token={WebUtility.UrlEncode(rawToken)}";

            await _email.SendAsync(user.Email, "Reset your OmniNode password",
                $"<p>Someone asked to reset the password for your OmniNode account.</p>" +
                $"<p><a href=\"{link}\">Reset your password</a> (valid for 1 hour).</p>" +
                $"<p>If this wasn't you, ignore this email.</p>");
        }

        return Accepted();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < MinPasswordLength)
            return BadRequest(new { Error = $"New password must be at least {MinPasswordLength} characters." });
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { Error = "This reset link is invalid or has expired." });

        var hash = ApiKeys.Hash(request.Token.Trim());
        var now = DateTime.UtcNow;

        var token = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now);
        if (token == null)
            return BadRequest(new { Error = "This reset link is invalid or has expired." });

        var user = await _context.Users.FindAsync(token.UserId);
        if (user == null || !user.IsActive)
            return BadRequest(new { Error = "This reset link is invalid or has expired." });

        user.PasswordHash = _hasher.HashPassword(request.NewPassword);
        token.UsedAt = now;
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public record RegisterRequest(string Email, string Password, UserRole Role, string? OrganizationName, Guid? AgencyId);
public record LoginRequest(string Email, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record UpdateProfileRequest(string? OrganizationName, string? OwnerName);
public record ChangeEmailRequest(string NewEmail, string Password);
