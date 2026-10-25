using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public record AgencyLimitsRequest(int? MonthlyTaskQuota, int? MonthlyAiQuota, int? MaxSubTenants, bool? WhiteLabel);
public record AccountStatusRequest(string Status, string? Reason);
public record ExtendRequest(int Days, string? Reason);
public record SetPlanRequest(string PlanId, int? Days, string? Reason);
public record DeleteAccountRequest(string ConfirmName);

/// <summary>SuperAdmin console API. Every change is written to AdminAuditLogs.</summary>
[Authorize(Roles = "SuperAdmin")]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private static readonly string[] AllowedStatuses = { "Active", "Suspended" };
    private const string Failed = "Failed_ManualReviewRequired";
    private readonly AppDbContext _context;

    public AdminController(AppDbContext context)
    {
        _context = context;
    }

    // ================= overview =================

    [HttpGet("telemetry")]
    public async Task<IActionResult> GetTelemetry()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var weekAgo = now.AddDays(-7);

        var tenants = await _context.Tenants.AsNoTracking()
            .Select(t => new { t.AgencyId, t.SubscriptionTier, t.BillingStatus, t.TasksUsedThisMonth, t.AiTasksUsedThisMonth })
            .ToListAsync();
        var agencies = await _context.Agencies.AsNoTracking()
            .Select(a => new { a.SubscriptionTier, a.BillingStatus, a.WhiteLabel })
            .ToListAsync();

        var paid = new[] { "Active", "GracePeriod" };
        var mrrTenants = tenants.Where(t => t.AgencyId == null && paid.Contains(t.BillingStatus))
            .Sum(t => Plans.Find(t.SubscriptionTier)?.PriceJod ?? 0m);
        var mrrAgencies = agencies.Where(a => paid.Contains(a.BillingStatus))
            .Sum(a => (AgencyPlans.Find(a.SubscriptionTier)?.PriceJod ?? 0m) +
                      (a.WhiteLabel && AgencyPlans.Find(a.SubscriptionTier) is { WhiteLabelIncluded: false } ? AgencyPlans.WhiteLabelAddOnJod : 0m));

        return Ok(new
        {
            Accounts = new
            {
                Merchants = tenants.Count(t => t.AgencyId == null),
                AgencyClients = tenants.Count(t => t.AgencyId != null),
                Agencies = agencies.Count,
                Users = await _context.Users.CountAsync(),
                SignupsLast7Days = await _context.Users.CountAsync(u => u.CreatedAt >= weekAgo)
            },
            Status = new
            {
                MerchantsByStatus = tenants.Where(t => t.AgencyId == null).GroupBy(t => t.BillingStatus).ToDictionary(g => g.Key, g => g.Count()),
                AgenciesByStatus = agencies.GroupBy(a => a.BillingStatus).ToDictionary(g => g.Key, g => g.Count())
            },
            Revenue = new
            {
                MrrJod = mrrTenants + mrrAgencies,
                CollectedThisMonthJod = await _context.PaymentInvoices
                    .Where(i => i.Status == "Confirmed" && i.PaidAt >= monthStart).SumAsync(i => (decimal?)i.Amount) ?? 0m,
                PendingInvoices = await _context.PaymentInvoices.CountAsync(i => i.Status == "Unpaid"),
                ReportedPayments = await _context.PaymentInvoices.CountAsync(i => i.Status == "Unpaid" && i.PaymentReportedAt != null),
                UnpaidTotalJod = await _context.PaymentInvoices.Where(i => i.Status == "Unpaid").SumAsync(i => (decimal?)i.Amount) ?? 0m
            },
            Usage = new
            {
                TasksThisMonth = tenants.Sum(t => t.TasksUsedThisMonth),
                AiTasksThisMonth = tenants.Sum(t => t.AiTasksUsedThisMonth),
                EventsLast24h = await _context.RawEvents.CountAsync(e => e.ReceivedAt >= now.AddDays(-1))
            },
            Queue = new
            {
                Pending = await _context.RawEvents.CountAsync(e => e.Status == "Pending" || e.Status == "Processing"),
                DeadLetter = await _context.RawEvents.CountAsync(e => e.Status == Failed)
            }
        });
    }

    /// <summary>Accounts due within N days, overdue, in grace, suspended, or trials ending; plus reported payments.</summary>
    [HttpGet("renewals")]
    public async Task<IActionResult> Renewals([FromQuery] int days = 7)
    {
        var until = DateTime.UtcNow.AddDays(Math.Clamp(days, 1, 60));

        var merchants = await _context.Tenants.AsNoTracking()
            .Where(t => t.AgencyId == null && t.NextRenewalDate < until && t.BillingStatus != "Paused")
            .Select(t => new { Type = "Tenant", t.Id, t.Name, Email = t.OwnerEmail, Plan = t.SubscriptionTier, t.BillingStatus, t.NextRenewalDate })
            .ToListAsync();
        var agencies = await _context.Agencies.AsNoTracking()
            .Where(a => a.NextRenewalDate < until)
            .Select(a => new { Type = "Agency", a.Id, a.Name, Email = a.OwnerEmail, Plan = a.SubscriptionTier, a.BillingStatus, a.NextRenewalDate })
            .ToListAsync();

        var accountIds = merchants.Select(m => m.Id).Concat(agencies.Select(a => a.Id)).ToList();
        var openInvoices = await _context.PaymentInvoices.AsNoTracking()
            .Where(i => i.Status == "Unpaid" && accountIds.Contains(i.AccountId))
            .Select(i => new { i.AccountId, i.Id, i.ReferenceCode, i.PaymentReportedAt })
            .ToListAsync();

        var items = merchants.Concat(agencies)
            .OrderBy(x => x.NextRenewalDate)
            .Select(x =>
            {
                var inv = openInvoices.Where(i => i.AccountId == x.Id).OrderByDescending(i => i.PaymentReportedAt).FirstOrDefault();
                return new
                {
                    x.Type, x.Id, x.Name, x.Email, x.Plan, x.BillingStatus, x.NextRenewalDate,
                    DaysLeft = (int)Math.Floor((x.NextRenewalDate - DateTime.UtcNow).TotalDays),
                    OpenInvoiceId = inv?.Id,
                    OpenInvoiceRef = inv?.ReferenceCode,
                    PaymentReported = inv?.PaymentReportedAt != null
                };
            });

        return Ok(items);
    }

    // ================= invoices =================

    /// <summary>status = pending | reported | approved | rejected | all. Search matches reference code or account name.</summary>
    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices([FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        (page, pageSize) = Paging(page, pageSize);
        var q = _context.PaymentInvoices.AsNoTracking().AsQueryable();
        q = (status ?? "pending").ToLowerInvariant() switch
        {
            "pending" => q.Where(i => i.Status == "Unpaid"),
            "reported" => q.Where(i => i.Status == "Unpaid" && i.PaymentReportedAt != null),
            "approved" => q.Where(i => i.Status == "Confirmed"),
            "rejected" => q.Where(i => i.Status == "Rejected"),
            _ => q
        };

        var names = await AccountNames();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var matchingIds = names.Where(n => n.Value.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(n => n.Key).ToList();
            q = q.Where(i => EF.Functions.ILike(i.ReferenceCode, $"%{term}%") || matchingIds.Contains(i.AccountId));
        }

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(i => i.PaymentReportedAt != null).ThenByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return Ok(new
        {
            Total = total, Page = page, PageSize = pageSize,
            Items = items.Select(i => new
            {
                i.Id, i.ReferenceCode, i.AccountType, i.AccountId,
                AccountName = names.GetValueOrDefault(i.AccountId),
                i.PlanId,
                PlanName = Plans.Find(i.PlanId)?.Name ?? AgencyPlans.Find(i.PlanId)?.Name,
                i.WhiteLabelAddOn, i.Amount, i.Currency, i.PaymentMethod,
                Status = i.Status switch { "Confirmed" => "Approved", "Rejected" => "Rejected", _ => "Pending" },
                i.CreatedAt, i.PaymentReportedAt, ApprovedAt = i.PaidAt
            })
        });
    }
    // Approve / reject: POST /api/Billing/approve-invoice/{id}, /api/Billing/reject-invoice/{id} (audited)

    // ================= accounts =================

    /// <summary>type = merchant | client | all; status filter; search by name or email.</summary>
    [HttpGet("tenants")]
    public async Task<IActionResult> Tenants([FromQuery] string? type, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        (page, pageSize) = Paging(page, pageSize);
        var q = _context.Tenants.AsNoTracking().AsQueryable();
        q = (type ?? "merchant").ToLowerInvariant() switch
        {
            "merchant" => q.Where(t => t.AgencyId == null),
            "client" => q.Where(t => t.AgencyId != null),
            _ => q
        };
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.BillingStatus == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            q = q.Where(t => EF.Functions.ILike(t.Name, term) || EF.Functions.ILike(t.OwnerEmail, term));
        }

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(t => t.TasksUsedThisMonth)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new
            {
                t.Id, t.Name, t.OwnerEmail, t.SubscriptionTier, t.BillingStatus, t.NextRenewalDate, t.AgencyId,
                AgencyName = t.Agency != null ? t.Agency.Name : null,
                t.MonthlyTaskQuota, t.TasksUsedThisMonth, t.AiTasksQuota, t.AiTasksUsedThisMonth,
                EndpointCount = _context.Endpoints.Count(e => e.TenantId == t.Id),
                FailedEvents = _context.RawEvents.Count(e => e.TenantId == t.Id && e.Status == Failed),
                LastEventAt = _context.RawEvents.Where(e => e.TenantId == t.Id).OrderByDescending(e => e.ReceivedAt)
                    .Select(e => (DateTime?)e.ReceivedAt).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
    }

    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> TenantDetail(Guid id)
    {
        var t = await _context.Tenants.AsNoTracking().Include(x => x.Agency).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound(new { Error = "Tenant not found." });

        return Ok(new
        {
            t.Id, t.Name, t.OwnerName, t.OwnerEmail, t.SubscriptionTier, t.BillingStatus, t.NextRenewalDate,
            t.TenantType, t.AgencyId, AgencyName = t.Agency?.Name, t.ApiKeyHint,
            t.MonthlyTaskQuota, t.TasksUsedThisMonth, t.AiTasksQuota, t.AiTasksUsedThisMonth,
            Users = await _context.Users.AsNoTracking().Where(u => u.TenantId == id)
                .Select(u => new { u.Id, u.Email, Role = u.Role.ToString(), u.IsActive, u.CreatedAt, u.LastLoginAt }).ToListAsync(),
            Endpoints = await _context.Endpoints.CountAsync(e => e.TenantId == id),
            Events = new
            {
                Total = await _context.RawEvents.CountAsync(e => e.TenantId == id),
                Failed = await _context.RawEvents.CountAsync(e => e.TenantId == id && e.Status == Failed),
                Last = await _context.RawEvents.Where(e => e.TenantId == id).OrderByDescending(e => e.ReceivedAt)
                    .Select(e => (DateTime?)e.ReceivedAt).FirstOrDefaultAsync()
            },
            Invoices = await InvoicesFor(id, "Tenant"),
            Audit = await AuditFor(id)
        });
    }

    [HttpGet("agencies")]
    public async Task<IActionResult> GetAllAgencies([FromQuery] string? status, [FromQuery] string? search)
    {
        var q = _context.Agencies.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.BillingStatus == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            q = q.Where(a => EF.Functions.ILike(a.Name, term) || EF.Functions.ILike(a.OwnerEmail, term));
        }

        return Ok(await q.OrderBy(a => a.Name).Select(a => new
        {
            a.Id, a.Name, a.OwnerEmail, a.SubscriptionTier, a.BillingStatus, a.NextRenewalDate, a.WhiteLabel,
            a.MaxSubTenants, CurrentSubTenants = a.Tenants.Count,
            a.MonthlyTaskQuota, a.TasksUsedThisMonth, a.MonthlyAiQuota, a.AiTasksUsedThisMonth, a.CreatedAt
        }).ToListAsync());
    }

    [HttpGet("agencies/{id:guid}")]
    public async Task<IActionResult> AgencyDetail(Guid id)
    {
        var a = await _context.Agencies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound(new { Error = "Agency not found." });

        return Ok(new
        {
            a.Id, a.Name, a.OwnerEmail, a.SubscriptionTier, a.BillingStatus, a.NextRenewalDate, a.CreatedAt,
            a.MaxSubTenants, a.MonthlyTaskQuota, a.TasksUsedThisMonth, a.MonthlyAiQuota, a.AiTasksUsedThisMonth,
            a.WhiteLabel, a.BrandName,
            Clients = await _context.Tenants.AsNoTracking().Where(t => t.AgencyId == id).OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name, t.BillingStatus, t.MonthlyTaskQuota, t.TasksUsedThisMonth, t.AiTasksQuota, t.AiTasksUsedThisMonth })
                .ToListAsync(),
            Users = await _context.Users.AsNoTracking().Where(u => u.AgencyId == id)
                .Select(u => new { u.Id, u.Email, Role = u.Role.ToString(), u.IsActive, u.CreatedAt, u.LastLoginAt }).ToListAsync(),
            Invoices = await InvoicesFor(id, "Agency"),
            Audit = await AuditFor(id)
        });
    }

    /// <summary>type = tenant | agency. Status Active or Suspended (manual override).</summary>
    [HttpPost("accounts/{type}/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(string type, Guid id, [FromBody] AccountStatusRequest request)
    {
        if (!AllowedStatuses.Contains(request.Status))
            return BadRequest(new { Error = "status must be Active or Suspended." });

        var (ok, name, apply) = await Account(type, id);
        if (!ok) return NotFound(new { Error = "Account not found." });

        apply(a => a.SetStatus(request.Status));
        AdminAudit.Log(_context, User, $"account.{request.Status.ToLowerInvariant()}", Cap(type), id, $"{name}. {request.Reason}");
        await _context.SaveChangesAsync();
        return Ok(new { Id = id, request.Status });
    }

    /// <summary>Pushes the renewal (or trial end) date forward, e.g. a goodwill extension.</summary>
    [HttpPost("accounts/{type}/{id:guid}/extend")]
    public async Task<IActionResult> Extend(string type, Guid id, [FromBody] ExtendRequest request)
    {
        if (request.Days is < 1 or > 365) return BadRequest(new { Error = "days must be 1-365." });

        var (ok, name, apply) = await Account(type, id);
        if (!ok) return NotFound(new { Error = "Account not found." });

        DateTime newDate = default;
        apply(a => newDate = a.Extend(request.Days));
        AdminAudit.Log(_context, User, "account.extend", Cap(type), id, $"{name}: +{request.Days} days to {newDate:yyyy-MM-dd}. {request.Reason}");
        await _context.SaveChangesAsync();
        return Ok(new { Id = id, NextRenewalDate = newDate });
    }

    /// <summary>Sets a plan without an invoice (comp, migration, correction). Active for N days (default 30).</summary>
    [HttpPut("accounts/{type}/{id:guid}/plan")]
    public async Task<IActionResult> SetPlan(string type, Guid id, [FromBody] SetPlanRequest request)
    {
        var days = request.Days ?? 30;
        if (days is < 1 or > 365) return BadRequest(new { Error = "days must be 1-365." });
        var now = DateTime.UtcNow;

        if (type.Equals("tenant", StringComparison.OrdinalIgnoreCase))
        {
            var t = await _context.Tenants.FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound(new { Error = "Tenant not found." });
            if (t.AgencyId != null) return BadRequest(new { Error = "Agency clients follow their agency's plan." });
            var plan = Plans.Find(request.PlanId);
            if (plan == null) return BadRequest(new { Error = "Unknown planId." });

            Plans.Apply(t, plan);
            t.BillingStatus = plan.Id == "free" ? "Trial" : "Active";
            t.NextRenewalDate = now.AddDays(days);
            AdminAudit.Log(_context, User, "account.set_plan", "Tenant", id, $"{t.Name}: {plan.Name} for {days} days. {request.Reason}");
        }
        else if (type.Equals("agency", StringComparison.OrdinalIgnoreCase))
        {
            var a = await _context.Agencies.FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound(new { Error = "Agency not found." });
            var plan = AgencyPlans.Find(request.PlanId);
            if (plan == null) return BadRequest(new { Error = "Unknown planId." });

            AgencyPlans.Apply(a, plan);
            a.BillingStatus = plan.PriceJod == 0m ? "Trial" : "Active";
            a.NextRenewalDate = now.AddDays(days);
            foreach (var c in await _context.Tenants.Where(x => x.AgencyId == id).ToListAsync())
            {
                c.SubscriptionTier = a.SubscriptionTier;
                c.NextRenewalDate = a.NextRenewalDate;
            }
            AdminAudit.Log(_context, User, "account.set_plan", "Agency", id, $"{a.Name}: {plan.Name} for {days} days. {request.Reason}");
        }
        else return BadRequest(new { Error = "type must be tenant or agency." });

        await _context.SaveChangesAsync();
        return Ok(new { Id = id, request.PlanId, Days = days });
    }

    /// <summary>Custom limits for an agency (e.g. a negotiated Platinum deal). Null fields unchanged.</summary>
    [HttpPut("agencies/{agencyId:guid}/limits")]
    public async Task<IActionResult> SetAgencyLimits(Guid agencyId, [FromBody] AgencyLimitsRequest request)
    {
        var agency = await _context.Agencies.FindAsync(agencyId);
        if (agency == null) return NotFound(new { Error = "Agency not found." });

        if (request.MonthlyTaskQuota is < 0 || request.MonthlyAiQuota is < 0 || request.MaxSubTenants is < 0)
            return BadRequest(new { Error = "Limits can't be negative." });

        if (request.MonthlyTaskQuota.HasValue) agency.MonthlyTaskQuota = request.MonthlyTaskQuota.Value;
        if (request.MonthlyAiQuota.HasValue) agency.MonthlyAiQuota = request.MonthlyAiQuota.Value;
        if (request.MaxSubTenants.HasValue) agency.MaxSubTenants = request.MaxSubTenants.Value;
        if (request.WhiteLabel.HasValue) agency.WhiteLabel = request.WhiteLabel.Value;

        AdminAudit.Log(_context, User, "agency.limits", "Agency", agencyId,
            $"tasks={agency.MonthlyTaskQuota} ai={agency.MonthlyAiQuota} clients={agency.MaxSubTenants} whiteLabel={agency.WhiteLabel}");
        await _context.SaveChangesAsync();
        return Ok(new { agency.Id, agency.Name, agency.MonthlyTaskQuota, agency.MonthlyAiQuota, agency.MaxSubTenants, agency.WhiteLabel });
    }

    /// <summary>
    /// Permanently deletes a merchant (with its users) or an agency (with all its clients and users), plus invoices.
    /// Body must repeat the exact account name. For cleaning test data and honoring deletion requests.
    /// </summary>
    [HttpDelete("accounts/{type}/{id:guid}")]
    public async Task<IActionResult> DeleteAccount(string type, Guid id, [FromBody] DeleteAccountRequest request)
    {
        List<Guid> tenantIds;
        Guid? agencyId = null;
        string name;

        if (type.Equals("tenant", StringComparison.OrdinalIgnoreCase))
        {
            var t = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound(new { Error = "Tenant not found." });
            if (t.AgencyId != null) return BadRequest(new { Error = "Delete agency clients from the agency portal or delete the agency." });
            name = t.Name;
            tenantIds = new List<Guid> { id };
        }
        else if (type.Equals("agency", StringComparison.OrdinalIgnoreCase))
        {
            var a = await _context.Agencies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound(new { Error = "Agency not found." });
            name = a.Name;
            agencyId = id;
            tenantIds = await _context.Tenants.Where(t => t.AgencyId == id).Select(t => t.Id).ToListAsync();
        }
        else return BadRequest(new { Error = "type must be tenant or agency." });

        if (!string.Equals(request.ConfirmName?.Trim(), name, StringComparison.Ordinal))
            return BadRequest(new { Error = "confirmName must match the account name exactly." });

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var endpointIds = await _context.Endpoints.Where(e => tenantIds.Contains(e.TenantId)).Select(e => e.Id).ToListAsync();
            var connectionIds = await _context.Connections
                .Where(c => endpointIds.Contains(c.SourceEndpointId) || endpointIds.Contains(c.DestinationEndpointId))
                .Select(c => c.Id).ToListAsync();
            var userIds = await _context.Users
                .Where(u => (u.TenantId != null && tenantIds.Contains(u.TenantId.Value)) || (agencyId != null && u.AgencyId == agencyId))
                .Select(u => u.Id).ToListAsync();
            var accountIds = tenantIds.Concat(agencyId.HasValue ? new[] { agencyId.Value } : Array.Empty<Guid>()).ToList();

            await _context.Mappings.Where(m => connectionIds.Contains(m.ConnectionId)).ExecuteDeleteAsync();
            await _context.Connections.Where(c => connectionIds.Contains(c.Id)).ExecuteDeleteAsync();
            await _context.TenantCredentials.Where(c => tenantIds.Contains(c.TenantId)).ExecuteDeleteAsync();
            await _context.RawEvents.Where(e => tenantIds.Contains(e.TenantId)).ExecuteDeleteAsync();
            await _context.FieldMappingRules.Where(r => endpointIds.Contains(r.EndpointId)).ExecuteDeleteAsync();
            await _context.Endpoints.Where(e => tenantIds.Contains(e.TenantId)).ExecuteDeleteAsync();
            await _context.DeviceTokens.Where(d => userIds.Contains(d.UserId)).ExecuteDeleteAsync();
            await _context.PasswordResetTokens.Where(p => userIds.Contains(p.UserId)).ExecuteDeleteAsync();
            await _context.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
            await _context.PaymentInvoices.Where(i => accountIds.Contains(i.AccountId)).ExecuteDeleteAsync();
            await _context.ReminderLogs.Where(r => accountIds.Contains(r.AccountId)).ExecuteDeleteAsync();
            await _context.UsageLedgers.Where(l => (l.TenantId != null && tenantIds.Contains(l.TenantId.Value)) ||
                                                   (agencyId != null && l.AgencyId == agencyId)).ExecuteDeleteAsync();
            await _context.Tenants.Where(t => tenantIds.Contains(t.Id)).ExecuteDeleteAsync();
            if (agencyId.HasValue) await _context.Agencies.Where(a => a.Id == agencyId.Value).ExecuteDeleteAsync();

            AdminAudit.Log(_context, User, "account.delete", Cap(type), id, $"{name} ({tenantIds.Count} workspace(s), {userIds.Count} user(s))");
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        });

        return NoContent();
    }

    // ================= users =================

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? search, [FromQuery] string? role,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        (page, pageSize) = Paging(page, pageSize);
        var q = _context.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(u => EF.Functions.ILike(u.Email, $"%{search.Trim()}%"));
        if (Enum.TryParse<UserRole>(role, true, out var r)) q = q.Where(u => u.Role == r);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new
            {
                u.Id, u.Email, Role = u.Role.ToString(), u.IsActive, u.CreatedAt, u.LastLoginAt,
                u.TenantId, TenantName = u.Tenant != null ? u.Tenant.Name : null,
                u.AgencyId, AgencyName = u.Agency != null ? u.Agency.Name : null
            }).ToListAsync();

        return Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
    }

    /// <summary>Blocks login. Existing sessions end when their token expires (max 7 days).</summary>
    [HttpPost("users/{id:guid}/deactivate")]
    public Task<IActionResult> Deactivate(Guid id) => SetUserActive(id, false);

    [HttpPost("users/{id:guid}/activate")]
    public Task<IActionResult> Activate(Guid id) => SetUserActive(id, true);

    private async Task<IActionResult> SetUserActive(Guid id, bool active)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound(new { Error = "User not found." });
        if (user.Role == UserRole.SuperAdmin && !active) return BadRequest(new { Error = "SuperAdmin accounts can't be deactivated here." });

        user.IsActive = active;
        AdminAudit.Log(_context, User, active ? "user.activate" : "user.deactivate", "User", id, user.Email);
        await _context.SaveChangesAsync();
        return Ok(new { user.Id, user.Email, user.IsActive });
    }

    // ================= events / DLQ =================

    [HttpGet("dead-letter-queue")]
    public async Task<IActionResult> GetDeadLetterQueue([FromQuery] Guid? tenantId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        (page, pageSize) = Paging(page, pageSize);
        var q = _context.RawEvents.AsNoTracking().Where(e => e.Status == Failed);
        if (tenantId.HasValue) q = q.Where(e => e.TenantId == tenantId.Value);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(e => EF.Functions.ILike(e.FailureReason, $"%{search.Trim()}%"));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.LastAttemptAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new { e.Id, e.TenantId, e.EndpointId, e.ReceivedAt, e.LastAttemptAt, e.RetryCount, e.FailureReason, e.IsAi, e.Payload })
            .ToListAsync();
        var names = await AccountNames();

        return Ok(new
        {
            Total = total, Page = page, PageSize = pageSize,
            Items = items.Select(e => new
            {
                e.Id, e.TenantId, TenantName = names.GetValueOrDefault(e.TenantId), e.EndpointId,
                e.ReceivedAt, e.LastAttemptAt, e.RetryCount, e.FailureReason, e.IsAi, e.Payload
            })
        });
    }

    [HttpPost("events/{id:guid}/retry")]
    public async Task<IActionResult> RetryEvent(Guid id)
    {
        var count = await Requeue(_context.RawEvents.Where(e => e.Id == id));
        if (count == 0) return NotFound(new { Error = "Failed event not found." });
        AdminAudit.Log(_context, User, "event.retry", "Event", id);
        await _context.SaveChangesAsync();
        return Ok(new { Requeued = count });
    }

    /// <summary>Bulk re-queue (optionally one endpoint or one tenant).</summary>
    [HttpPost("reprocess-failed")]
    public async Task<IActionResult> ReprocessFailedEvents([FromQuery] Guid? endpointId = null, [FromQuery] Guid? tenantId = null)
    {
        var q = _context.RawEvents.AsQueryable();
        if (endpointId.HasValue) q = q.Where(e => e.EndpointId == endpointId.Value);
        if (tenantId.HasValue) q = q.Where(e => e.TenantId == tenantId.Value);

        var count = await Requeue(q);
        AdminAudit.Log(_context, User, "event.retry_bulk", "Event", null, $"endpoint={endpointId} tenant={tenantId} count={count}");
        await _context.SaveChangesAsync();
        return Ok(new { Requeued = count, Message = $"Successfully re-queued {count} failed event(s)." });
    }

    // ================= audit =================

    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] string? action, [FromQuery] Guid? targetId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        (page, pageSize) = Paging(page, pageSize);
        var q = _context.AdminAuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.StartsWith(action));
        if (targetId.HasValue) q = q.Where(a => a.TargetId == targetId);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.At).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
    }

    // ================= helpers =================

    private static (int, int) Paging(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 200));

    private static string Cap(string type) => type.Equals("agency", StringComparison.OrdinalIgnoreCase) ? "Agency" : "Tenant";

    private static Task<int> Requeue(IQueryable<RawEvent> q) =>
        q.Where(e => e.Status == Failed).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, "Pending")
            .SetProperty(e => e.RetryCount, 0)
            .SetProperty(e => e.LastAttemptAt, (DateTime?)null)
            .SetProperty(e => e.FailureReason, string.Empty));

    private async Task<Dictionary<Guid, string>> AccountNames()
    {
        var names = await _context.Tenants.AsNoTracking().Select(t => new { t.Id, t.Name }).ToDictionaryAsync(t => t.Id, t => t.Name);
        foreach (var a in await _context.Agencies.AsNoTracking().Select(a => new { a.Id, a.Name }).ToListAsync())
            names[a.Id] = a.Name;
        return names;
    }

    private async Task<object> InvoicesFor(Guid accountId, string accountType) =>
        await _context.PaymentInvoices.AsNoTracking()
            .Where(i => i.AccountId == accountId && i.AccountType == accountType)
            .OrderByDescending(i => i.CreatedAt).Take(24)
            .Select(i => new { i.Id, i.ReferenceCode, i.PlanId, i.Amount, i.Currency, i.Status, i.CreatedAt, i.PaymentReportedAt, i.PaidAt })
            .ToListAsync();

    private async Task<object> AuditFor(Guid targetId) =>
        await _context.AdminAuditLogs.AsNoTracking().Where(a => a.TargetId == targetId)
            .OrderByDescending(a => a.At).Take(20).ToListAsync();

    /// <summary>Loads a tenant (standalone) or agency and returns a mutator over a small common interface.</summary>
    private async Task<(bool Ok, string Name, Action<Action<IBillable>> Apply)> Account(string type, Guid id)
    {
        Action<Action<IBillable>> none = _ => { };

        if (type.Equals("tenant", StringComparison.OrdinalIgnoreCase))
        {
            var t = await _context.Tenants.FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return (false, "", none);
            Action<Action<IBillable>> applyTenant = f => f(new TenantBillable(t));
            return (true, t.Name, applyTenant);
        }
        if (type.Equals("agency", StringComparison.OrdinalIgnoreCase))
        {
            var a = await _context.Agencies.FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return (false, "", none);
            Action<Action<IBillable>> applyAgency = f => f(new AgencyBillable(a));
            return (true, a.Name, applyAgency);
        }
        return (false, "", none);
    }

    private interface IBillable
    {
        void SetStatus(string status);
        DateTime Extend(int days);
    }

    private sealed class TenantBillable : IBillable
    {
        private readonly Tenant _t;
        public TenantBillable(Tenant t) => _t = t;
        public void SetStatus(string status) => _t.BillingStatus = status;
        public DateTime Extend(int days)
        {
            var now = DateTime.UtcNow;
            _t.NextRenewalDate = (_t.NextRenewalDate > now ? _t.NextRenewalDate : now).AddDays(days);
            if (_t.BillingStatus is "GracePeriod" or "Suspended") _t.BillingStatus = "Active";
            if (_t.BillingStatus == "TrialExpired") _t.BillingStatus = "Trial";
            return _t.NextRenewalDate;
        }
    }

    private sealed class AgencyBillable : IBillable
    {
        private readonly Agency _a;
        public AgencyBillable(Agency a) => _a = a;
        public void SetStatus(string status) => _a.BillingStatus = status;
        public DateTime Extend(int days)
        {
            var now = DateTime.UtcNow;
            _a.NextRenewalDate = (_a.NextRenewalDate > now ? _a.NextRenewalDate : now).AddDays(days);
            if (_a.BillingStatus is "GracePeriod" or "Suspended") _a.BillingStatus = "Active";
            if (_a.BillingStatus == "TrialExpired") _a.BillingStatus = "Trial";
            return _a.NextRenewalDate;
        }
    }
}
