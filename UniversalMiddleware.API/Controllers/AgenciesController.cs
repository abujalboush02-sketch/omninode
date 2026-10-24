using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public record CreateSubTenantRequest(string Name, string? OwnerEmail, int? AllocatedTaskQuota, int? AllocatedAiQuota);
public record UpdateQuotaRequest(int? MonthlyTaskQuota, int? AiTasksQuota, int? AllocatedTaskQuota, int? AllocatedAiQuota);
public record UpdateSubTenantRequest(string? Name, string? OwnerEmail);
public record BrandingRequest(string? BrandName, string? BrandLogoDataUrl, string? BrandColor, string? ReportFooter);

/// <summary>
/// Agency portal. Agencies manage their clients (sub-tenants) here; inside a client they use the normal
/// tenant endpoints with the X-Tenant-Id header (see ApiKeyAuthAttribute).
/// Quota model: pooled. Sum of client allocations may not exceed the agency's plan.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AgenciesController : ControllerBase
{
    private const int MaxLogoChars = 280_000; // ~200 KB image as base64
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly string[] FailedStatuses = { "Failed_ManualReviewRequired" };

    private readonly AppDbContext _context;

    public AgenciesController(AppDbContext context)
    {
        _context = context;
    }

    // ---------- caller ----------

    private (UserRole Role, Guid? AgencyId) Caller()
    {
        Enum.TryParse<UserRole>(User.FindFirstValue(ClaimTypes.Role), out var role);
        Guid? agencyId = Guid.TryParse(User.FindFirstValue("AgencyId"), out var a) ? a : null;
        return (role, agencyId);
    }

    /// <summary>Returns the caller's agency (tracked), or an error result.</summary>
    private async Task<(Agency? Agency, IActionResult? Error)> MyAgency(bool includeTenants = false)
    {
        var (role, agencyId) = Caller();
        if (role != UserRole.AgencyOwner && role != UserRole.SuperAdmin)
            return (null, StatusCode(403, new { Error = "Agency owners only." }));
        if (!agencyId.HasValue)
            return (null, BadRequest(new { Error = "Caller is not associated with an agency." }));

        var query = _context.Agencies.AsQueryable();
        if (includeTenants) query = query.Include(a => a.Tenants);
        var agency = await query.FirstOrDefaultAsync(a => a.Id == agencyId.Value);
        return agency == null ? (null, NotFound(new { Error = "Agency not found." })) : (agency, null);
    }

    private static IActionResult? BlockIfSuspended(Agency agency, ControllerBase c) =>
        agency.BillingStatus switch
        {
            "Suspended" => c.StatusCode(402, new { Error = "Agency account is suspended due to unpaid invoices. Pay to make changes." }),
            "TrialExpired" => c.StatusCode(402, new { Error = "Your free trial has ended. Choose a plan to make changes." }),
            _ => null
        };

    // ---------- agency ----------

    /// <summary>Agency profile, plan, pooled usage and allocation totals.</summary>
    [HttpGet("my-agency")]
    public async Task<IActionResult> GetMyAgency()
    {
        var (agency, error) = await MyAgency(includeTenants: true);
        if (error != null) return error;

        var plan = AgencyPlans.Find(agency!.SubscriptionTier);
        var allocatedTasks = agency.Tenants.Sum(t => t.MonthlyTaskQuota);
        var allocatedAi = agency.Tenants.Sum(t => t.AiTasksQuota);

        return Ok(new
        {
            agency.Id,
            agency.Name,
            agency.OwnerEmail,
            agency.SubscriptionTier,
            PlanId = plan?.Id,
            agency.BillingStatus,
            agency.NextRenewalDate,
            agency.CreatedAt,
            agency.MaxSubTenants,
            SubTenantCount = agency.Tenants.Count,
            agency.MonthlyTaskQuota,
            agency.TasksUsedThisMonth,
            agency.MonthlyAiQuota,
            agency.AiTasksUsedThisMonth,
            AllocatedTaskQuota = allocatedTasks,
            AllocatedAiQuota = allocatedAi,
            UnallocatedTaskQuota = Math.Max(0, agency.MonthlyTaskQuota - allocatedTasks),
            UnallocatedAiQuota = Math.Max(0, agency.MonthlyAiQuota - allocatedAi),
            OverAllocated = allocatedTasks > agency.MonthlyTaskQuota || allocatedAi > agency.MonthlyAiQuota
                            || agency.Tenants.Count > agency.MaxSubTenants,
            agency.WhiteLabel,
            Branding = new { agency.BrandName, agency.BrandLogoDataUrl, agency.BrandColor, agency.ReportFooter }
        });
    }

    /// <summary>Dashboard data: last 7 days of events across all clients, by day and status, plus clients needing attention.</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        var since = DateTime.UtcNow.Date.AddDays(-6);
        var clientIds = await _context.Tenants.AsNoTracking()
            .Where(t => t.AgencyId == agency!.Id).Select(t => t.Id).ToListAsync();

        // One small grouped query per day (portable across timestamp column types)
        var daily = new List<(DateTime Day, string Status, int Count)>();
        for (var i = 0; i < 7; i++)
        {
            var dayStart = since.AddDays(i);
            var dayEnd = dayStart.AddDays(1);
            var counts = await _context.RawEvents.AsNoTracking()
                .Where(e => clientIds.Contains(e.TenantId) && e.ReceivedAt >= dayStart && e.ReceivedAt < dayEnd)
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            daily.AddRange(counts.Select(c => (dayStart, c.Status, c.Count)));
        }

        var failedByClient = await _context.RawEvents.AsNoTracking()
            .Where(e => clientIds.Contains(e.TenantId) && FailedStatuses.Contains(e.Status))
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Failed = g.Count() })
            .ToListAsync();

        var names = await _context.Tenants.AsNoTracking()
            .Where(t => clientIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.TasksUsedThisMonth, t.MonthlyTaskQuota, t.BillingStatus })
            .ToListAsync();

        var attention = names
            .Select(n => new
            {
                TenantId = n.Id,
                n.Name,
                Failed = failedByClient.FirstOrDefault(f => f.TenantId == n.Id)?.Failed ?? 0,
                UsagePercent = n.MonthlyTaskQuota == 0 ? 0 : (int)Math.Round(100.0 * n.TasksUsedThisMonth / n.MonthlyTaskQuota),
                n.BillingStatus
            })
            .Where(x => x.Failed > 0 || x.UsagePercent >= 80 || x.BillingStatus != "Active")
            .OrderByDescending(x => x.Failed).ThenByDescending(x => x.UsagePercent)
            .ToList();

        return Ok(new
        {
            Days = Enumerable.Range(0, 7).Select(i =>
            {
                var day = since.AddDays(i);
                var rows = daily.Where(d => d.Day == day).ToList();
                return new
                {
                    Date = day.ToString("yyyy-MM-dd"),
                    Processed = rows.Where(r => r.Status == "Processed").Sum(r => r.Count),
                    Failed = rows.Where(r => FailedStatuses.Contains(r.Status)).Sum(r => r.Count),
                    Pending = rows.Where(r => r.Status == "Pending" || r.Status == "Processing").Sum(r => r.Count)
                };
            }),
            TotalFailedOpen = failedByClient.Sum(f => f.Failed),
            NeedsAttention = attention
        });
    }

    // ---------- clients ----------

    [HttpGet("sub-tenants")]
    public async Task<IActionResult> GetSubTenants()
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        var tenants = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.AgencyId == agency!.Id)
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.OwnerEmail,
                t.MonthlyTaskQuota,
                t.TasksUsedThisMonth,
                t.AiTasksQuota,
                t.AiTasksUsedThisMonth,
                t.BillingStatus,
                t.ApiKeyHint,
                EndpointCount = _context.Endpoints.Count(e => e.TenantId == t.Id),
                ConnectionCount = _context.Connections.Count(c =>
                    _context.Endpoints.Any(e => e.Id == c.SourceEndpointId && e.TenantId == t.Id)),
                FailedEventCount = _context.RawEvents.Count(e => e.TenantId == t.Id && e.Status == "Failed_ManualReviewRequired"),
                LastEventAt = _context.RawEvents.Where(e => e.TenantId == t.Id)
                    .OrderByDescending(e => e.ReceivedAt).Select(e => (DateTime?)e.ReceivedAt).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(tenants);
    }

    /// <summary>Provisions a client. Returns its API key once.</summary>
    [HttpPost("sub-tenants")]
    public async Task<IActionResult> CreateSubTenant([FromBody] CreateSubTenantRequest request)
    {
        var (agency, error) = await MyAgency(includeTenants: true);
        if (error != null) return error;
        if (BlockIfSuspended(agency!, this) is { } blocked) return blocked;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { Error = "Client name is required." });

        if (agency!.Tenants.Count >= agency.MaxSubTenants)
            return BadRequest(new { Error = $"Client limit reached ({agency.MaxSubTenants}). Upgrade your agency plan to add more." });

        var tasks = request.AllocatedTaskQuota ?? 0;
        var ai = request.AllocatedAiQuota ?? 0;
        if (tasks < 0 || ai < 0) return BadRequest(new { Error = "Allocations can't be negative." });

        var poolError = CheckPool(agency, excludeTenantId: null, tasks, ai);
        if (poolError != null) return poolError;

        var (rawKey, hash) = ApiKeys.Generate();
        var name = request.Name.Trim();
        var hint = ApiKeys.Hint(rawKey);

        var tenant = new Tenant
        {
            Name = name,
            OwnerName = name,
            OwnerEmail = request.OwnerEmail?.Trim() ?? agency.OwnerEmail,
            PlanType = "AgencyManaged",
            TenantType = "AgencySubTenant",
            SubscriptionTier = agency.SubscriptionTier,
            ApiKeyHash = hash,
            ApiKeyHint = hint,
            AgencyId = agency.Id,
            MonthlyTaskQuota = tasks,
            AiTasksQuota = ai,
            BillingStatus = "Active",
            NextRenewalDate = agency.NextRenewalDate
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            tenant.Id,
            tenant.Name,
            tenant.OwnerEmail,
            tenant.MonthlyTaskQuota,
            tenant.AiTasksQuota,
            tenant.BillingStatus,
            ApiKey = rawKey // shown once
        });
    }

    [HttpPut("sub-tenants/{subTenantId:guid}")]
    public async Task<IActionResult> UpdateSubTenant(Guid subTenantId, [FromBody] UpdateSubTenantRequest request)
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == subTenantId && t.AgencyId == agency!.Id);
        if (tenant == null) return NotFound(new { Error = "Client not found within your agency." });

        if (!string.IsNullOrWhiteSpace(request.Name)) { tenant.Name = request.Name.Trim(); tenant.OwnerName = tenant.Name; }
        if (!string.IsNullOrWhiteSpace(request.OwnerEmail)) tenant.OwnerEmail = request.OwnerEmail.Trim();

        await _context.SaveChangesAsync();
        return Ok(new { tenant.Id, tenant.Name, tenant.OwnerEmail });
    }

    /// <summary>Sets a client's allocation. The total across clients can't exceed the agency plan.</summary>
    [HttpPut("sub-tenants/{subTenantId:guid}/quotas")]
    public async Task<IActionResult> UpdateSubTenantQuotas(Guid subTenantId, [FromBody] UpdateQuotaRequest request)
    {
        var (agency, error) = await MyAgency(includeTenants: true);
        if (error != null) return error;
        if (BlockIfSuspended(agency!, this) is { } blocked) return blocked;

        var tenant = agency!.Tenants.FirstOrDefault(t => t.Id == subTenantId);
        if (tenant == null) return NotFound(new { Error = "Client not found within your agency." });

        int? tasks = request.MonthlyTaskQuota ?? request.AllocatedTaskQuota;
        int? ai = request.AiTasksQuota ?? request.AllocatedAiQuota;
        if (tasks is null || ai is null)
            return BadRequest(new { Error = "Send monthlyTaskQuota and aiTasksQuota." });

        if (tasks < 0 || ai < 0)
            return BadRequest(new { Error = "Allocations can't be negative." });

        var poolError = CheckPool(agency, subTenantId, tasks.Value, ai.Value);
        if (poolError != null) return poolError;

        tenant.MonthlyTaskQuota = tasks.Value;
        tenant.AiTasksQuota = ai.Value;
        await _context.SaveChangesAsync();

        return Ok(new { tenant.Id, tenant.Name, tenant.MonthlyTaskQuota, tenant.AiTasksQuota, tenant.TasksUsedThisMonth });
    }

    /// <summary>Stops a client's webhooks (402) without deleting anything.</summary>
    [HttpPost("sub-tenants/{subTenantId:guid}/pause")]
    public Task<IActionResult> Pause(Guid subTenantId) => SetClientStatus(subTenantId, "Paused");

    [HttpPost("sub-tenants/{subTenantId:guid}/resume")]
    public Task<IActionResult> Resume(Guid subTenantId) => SetClientStatus(subTenantId, "Active");

    private async Task<IActionResult> SetClientStatus(Guid subTenantId, string status)
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == subTenantId && t.AgencyId == agency!.Id);
        if (tenant == null) return NotFound(new { Error = "Client not found within your agency." });

        tenant.BillingStatus = status;
        await _context.SaveChangesAsync();
        return Ok(new { tenant.Id, tenant.BillingStatus });
    }

    /// <summary>Permanently deletes a client and all its data (endpoints, events, mappings, credentials).</summary>
    [HttpDelete("sub-tenants/{subTenantId:guid}")]
    public async Task<IActionResult> DeleteSubTenant(Guid subTenantId)
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == subTenantId && t.AgencyId == agency!.Id);
        if (tenant == null) return NotFound(new { Error = "Client not found within your agency." });

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var endpointIds = await _context.Endpoints.Where(e => e.TenantId == subTenantId).Select(e => e.Id).ToListAsync();
            var connectionIds = await _context.Connections
                .Where(c => endpointIds.Contains(c.SourceEndpointId) || endpointIds.Contains(c.DestinationEndpointId))
                .Select(c => c.Id).ToListAsync();

            await _context.Mappings.Where(m => connectionIds.Contains(m.ConnectionId)).ExecuteDeleteAsync();
            await _context.Connections.Where(c => connectionIds.Contains(c.Id)).ExecuteDeleteAsync();
            await _context.TenantCredentials.Where(c => c.TenantId == subTenantId).ExecuteDeleteAsync();
            await _context.RawEvents.Where(e => e.TenantId == subTenantId).ExecuteDeleteAsync();
            await _context.FieldMappingRules.Where(r => endpointIds.Contains(r.EndpointId)).ExecuteDeleteAsync();
            await _context.Endpoints.Where(e => e.TenantId == subTenantId).ExecuteDeleteAsync();
            await _context.Users.Where(u => u.TenantId == subTenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TenantId, (Guid?)null));
            await _context.Tenants.Where(t => t.Id == subTenantId).ExecuteDeleteAsync();

            await tx.CommitAsync();
        });

        // Usage ledgers are kept for billing history.
        return NoContent();
    }

    // ---------- branding & reports ----------

    /// <summary>White-label branding for client reports. Requires Gold/Platinum or the white-label add-on.</summary>
    [HttpPut("branding")]
    public async Task<IActionResult> UpdateBranding([FromBody] BrandingRequest request)
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        if (!agency!.WhiteLabel)
            return StatusCode(403, new { Error = "White-label reports need Agency Gold, Platinum, or the white-label add-on." });

        if (request.BrandColor != null && request.BrandColor.Length > 0 && !HexColor.IsMatch(request.BrandColor))
            return BadRequest(new { Error = "brandColor must look like #1A2B3C." });

        if (request.BrandLogoDataUrl != null && request.BrandLogoDataUrl.Length > 0)
        {
            if (!request.BrandLogoDataUrl.StartsWith("data:image/png;base64,") &&
                !request.BrandLogoDataUrl.StartsWith("data:image/jpeg;base64,"))
                return BadRequest(new { Error = "Logo must be a PNG or JPEG data URL." });
            if (request.BrandLogoDataUrl.Length > MaxLogoChars)
                return BadRequest(new { Error = "Logo is too large (max ~200 KB)." });
        }

        agency.BrandName = Clean(request.BrandName, 100);
        agency.BrandLogoDataUrl = string.IsNullOrEmpty(request.BrandLogoDataUrl) ? null : request.BrandLogoDataUrl;
        agency.BrandColor = string.IsNullOrEmpty(request.BrandColor) ? null : request.BrandColor;
        agency.ReportFooter = Clean(request.ReportFooter, 300);

        await _context.SaveChangesAsync();
        return Ok(new { agency.BrandName, agency.BrandLogoDataUrl, agency.BrandColor, agency.ReportFooter });
    }

    /// <summary>
    /// Per-client usage for a month (yyyy-MM; default current). The frontend renders the PDF.
    /// Branding is returned only when the agency is entitled to white-label.
    /// </summary>
    [HttpGet("reports/usage")]
    public async Task<IActionResult> UsageReport([FromQuery] string? month, [FromQuery] Guid? tenantId)
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        var now = DateTime.UtcNow;
        var currentCycle = now.ToString("yyyy-MM");
        var cycle = string.IsNullOrWhiteSpace(month) ? currentCycle : month.Trim();
        if (!DateTime.TryParseExact(cycle + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
            return BadRequest(new { Error = "month must be yyyy-MM." });
        var end = start.AddMonths(1);

        var clients = await _context.Tenants.AsNoTracking()
            .Where(t => t.AgencyId == agency!.Id && (tenantId == null || t.Id == tenantId))
            .Select(t => new { t.Id, t.Name, t.MonthlyTaskQuota, t.AiTasksQuota, t.TasksUsedThisMonth, t.AiTasksUsedThisMonth })
            .ToListAsync();
        if (tenantId != null && clients.Count == 0) return NotFound(new { Error = "Client not found within your agency." });

        var ids = clients.Select(c => c.Id).ToList();

        // Task counters: live for the current month, ledger snapshot for past months
        var ledgers = cycle == currentCycle
            ? new Dictionary<Guid, (int Tasks, int Ai)>()
            : (await _context.UsageLedgers.AsNoTracking()
                .Where(l => l.TenantId != null && ids.Contains(l.TenantId.Value) && l.BillingCycle == cycle)
                .Select(l => new { l.TenantId, l.StandardTasksUsed, l.AiTasksUsed })
                .ToListAsync())
              .GroupBy(l => l.TenantId!.Value)
              .ToDictionary(g => g.Key, g => (g.Sum(x => x.StandardTasksUsed), g.Sum(x => x.AiTasksUsed)));

        var eventCounts = await _context.RawEvents.AsNoTracking()
            .Where(e => ids.Contains(e.TenantId) && e.ReceivedAt >= start && e.ReceivedAt < end)
            .GroupBy(e => new { e.TenantId, e.Status })
            .Select(g => new { g.Key.TenantId, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        var rows = clients.Select(c =>
        {
            int usedTasks = 0, usedAi = 0;
            if (cycle == currentCycle) { usedTasks = c.TasksUsedThisMonth; usedAi = c.AiTasksUsedThisMonth; }
            else if (ledgers.TryGetValue(c.Id, out var l)) { usedTasks = l.Item1; usedAi = l.Item2; }
            var ev = eventCounts.Where(e => e.TenantId == c.Id).ToList();
            return new
            {
                TenantId = c.Id,
                c.Name,
                TasksUsed = usedTasks,
                TaskAllocation = c.MonthlyTaskQuota,
                AiTasksUsed = usedAi,
                AiAllocation = c.AiTasksQuota,
                EventsReceived = ev.Sum(e => e.Count),
                EventsProcessed = ev.Where(e => e.Status == "Processed").Sum(e => e.Count),
                EventsFailed = ev.Where(e => FailedStatuses.Contains(e.Status)).Sum(e => e.Count)
            };
        }).OrderBy(r => r.Name).ToList();

        return Ok(new
        {
            Month = cycle,
            GeneratedAt = now,
            IsCurrentMonth = cycle == currentCycle,
            Agency = new { agency!.Name, agency.SubscriptionTier },
            Branding = agency.WhiteLabel
                ? new { agency.BrandName, agency.BrandLogoDataUrl, agency.BrandColor, agency.ReportFooter }
                : null,
            Clients = rows,
            Totals = new
            {
                TasksUsed = rows.Sum(r => r.TasksUsed),
                AiTasksUsed = rows.Sum(r => r.AiTasksUsed),
                EventsReceived = rows.Sum(r => r.EventsReceived),
                EventsProcessed = rows.Sum(r => r.EventsProcessed),
                EventsFailed = rows.Sum(r => r.EventsFailed)
            }
        });
    }

    // ---------- analytics ----------

    /// <summary>
    /// Time-series analytics across clients. from/to are UTC dates (to exclusive, max 366 days),
    /// tenantIds = comma-separated (empty = all clients), groupBy = day | week | month.
    /// One webhook = one task, so tasks per bucket = events received.
    /// </summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> Analytics([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? tenantIds, [FromQuery] string? groupBy)
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        var unit = (groupBy ?? "day").Trim().ToLowerInvariant();
        if (unit is not ("day" or "week" or "month"))
            return BadRequest(new { Error = "groupBy must be day, week or month." });

        var end = DateTime.SpecifyKind((to ?? DateTime.UtcNow.Date.AddDays(1)).Date, DateTimeKind.Utc);
        var start = DateTime.SpecifyKind((from ?? end.AddDays(-30)).Date, DateTimeKind.Utc);
        if (start >= end) return BadRequest(new { Error = "from must be before to." });
        if ((end - start).TotalDays > 366) return BadRequest(new { Error = "Range can't exceed 366 days." });

        var clients = await ClientsFor(agency!.Id, tenantIds);
        if (clients == null) return BadRequest(new { Error = "tenantIds must be client ids of your agency." });
        var ids = clients.Select(c => c.Id).ToArray();

        var rows = ids.Length == 0
            ? new List<BucketRow>()
            : await _context.Database.SqlQuery<BucketRow>($@"
                SELECT ""TenantId"" AS ""TenantId"",
                       date_trunc({unit}, ""ReceivedAt"" AT TIME ZONE 'UTC') AS ""Bucket"",
                       ""Status"" AS ""Status"",
                       ""IsAi"" AS ""IsAi"",
                       COUNT(*)::int AS ""Count""
                FROM ""RawEvents""
                WHERE ""TenantId"" = ANY({ids}) AND ""ReceivedAt"" >= {start} AND ""ReceivedAt"" < {end}
                GROUP BY 1, 2, 3, 4").ToListAsync();

        static object Sum(IEnumerable<BucketRow> r) => new
        {
            Tasks = r.Sum(x => x.Count),
            AiTasks = r.Where(x => x.IsAi).Sum(x => x.Count),
            Processed = r.Where(x => x.Status == "Processed").Sum(x => x.Count),
            Failed = r.Where(x => x.Status == "Failed_ManualReviewRequired").Sum(x => x.Count),
            Pending = r.Where(x => x.Status is "Pending" or "Processing").Sum(x => x.Count)
        };

        return Ok(new
        {
            From = start,
            To = end,
            GroupBy = unit,
            Totals = Sum(rows),
            Series = rows.GroupBy(r => r.Bucket).OrderBy(g => g.Key)
                .Select(g => new { Bucket = g.Key.ToString("yyyy-MM-dd"), Stats = Sum(g) }),
            Clients = clients.Select(c =>
            {
                var mine = rows.Where(r => r.TenantId == c.Id).ToList();
                return new
                {
                    TenantId = c.Id,
                    c.Name,
                    c.BillingStatus,
                    c.MonthlyTaskQuota,
                    c.TasksUsedThisMonth,
                    c.AiTasksQuota,
                    c.AiTasksUsedThisMonth,
                    Stats = Sum(mine),
                    Series = mine.GroupBy(r => r.Bucket).OrderBy(g => g.Key)
                        .Select(g => new { Bucket = g.Key.ToString("yyyy-MM-dd"), Stats = Sum(g) })
                };
            })
        });
    }

    /// <summary>Event search across clients (default: failed). Paged, newest first. Payload not included.</summary>
    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? tenantIds, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var (agency, error) = await MyAgency();
        if (error != null) return error;

        var clients = await ClientsFor(agency!.Id, tenantIds);
        if (clients == null) return BadRequest(new { Error = "tenantIds must be client ids of your agency." });
        var ids = clients.Select(c => c.Id).ToList();
        var names = clients.ToDictionary(c => c.Id, c => c.Name);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var wanted = (status ?? "failed").Trim().ToLowerInvariant();

        var q = _context.RawEvents.AsNoTracking().Where(e => ids.Contains(e.TenantId));
        q = wanted switch
        {
            "failed" => q.Where(e => e.Status == "Failed_ManualReviewRequired"),
            "processed" => q.Where(e => e.Status == "Processed"),
            "pending" => q.Where(e => e.Status == "Pending" || e.Status == "Processing"),
            _ => q
        };
        if (from.HasValue) { var f = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc); q = q.Where(e => e.ReceivedAt >= f); }
        if (to.HasValue) { var t = DateTime.SpecifyKind(to.Value, DateTimeKind.Utc); q = q.Where(e => e.ReceivedAt < t); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = Guid.TryParse(term, out var gid)
                ? q.Where(e => e.Id == gid || e.EndpointId == gid)
                : q.Where(e => EF.Functions.ILike(e.FailureReason, $"%{term}%"));
        }

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.ReceivedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new { e.Id, e.TenantId, e.EndpointId, e.Status, e.IsAi, e.ReceivedAt, e.LastAttemptAt, e.RetryCount, e.FailureReason })
            .ToListAsync();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items.Select(e => new
            {
                e.Id, e.TenantId, ClientName = names.GetValueOrDefault(e.TenantId),
                e.EndpointId, e.Status, e.IsAi, e.ReceivedAt, e.LastAttemptAt, e.RetryCount, e.FailureReason
            })
        });
    }

    /// <summary>Agency's clients, optionally narrowed to a comma-separated id list. Null if any id isn't theirs.</summary>
    private async Task<List<ClientInfo>?> ClientsFor(Guid agencyId, string? tenantIds)
    {
        var all = await _context.Tenants.AsNoTracking()
            .Where(t => t.AgencyId == agencyId)
            .Select(t => new ClientInfo(t.Id, t.Name, t.BillingStatus, t.MonthlyTaskQuota, t.TasksUsedThisMonth, t.AiTasksQuota, t.AiTasksUsedThisMonth))
            .ToListAsync();

        if (string.IsNullOrWhiteSpace(tenantIds)) return all;

        var wanted = new HashSet<Guid>();
        foreach (var part in tenantIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id) || all.All(c => c.Id != id)) return null;
            wanted.Add(id);
        }
        return all.Where(c => wanted.Contains(c.Id)).ToList();
    }

    private record ClientInfo(Guid Id, string Name, string BillingStatus, int MonthlyTaskQuota, int TasksUsedThisMonth, int AiTasksQuota, int AiTasksUsedThisMonth);

    // ---------- helpers ----------

    private IActionResult? CheckPool(Agency agency, Guid? excludeTenantId, int tasks, int ai)
    {
        var otherTasks = agency.Tenants.Where(t => t.Id != excludeTenantId).Sum(t => t.MonthlyTaskQuota);
        var otherAi = agency.Tenants.Where(t => t.Id != excludeTenantId).Sum(t => t.AiTasksQuota);

        if (otherTasks + tasks > agency.MonthlyTaskQuota)
            return BadRequest(new
            {
                Error = $"Not enough unallocated tasks. Available: {Math.Max(0, agency.MonthlyTaskQuota - otherTasks)}.",
                AvailableTasks = Math.Max(0, agency.MonthlyTaskQuota - otherTasks)
            });

        if (otherAi + ai > agency.MonthlyAiQuota)
            return BadRequest(new
            {
                Error = $"Not enough unallocated AI tasks. Available: {Math.Max(0, agency.MonthlyAiQuota - otherAi)}.",
                AvailableAiTasks = Math.Max(0, agency.MonthlyAiQuota - otherAi)
            });

        return null;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length > max ? v[..max] : v;
    }
}

/// <summary>Row shape for the analytics SQL query.</summary>
public class BucketRow
{
    public Guid TenantId { get; set; }
    public DateTime Bucket { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsAi { get; set; }
    public int Count { get; set; }
}
