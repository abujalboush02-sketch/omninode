using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.API.Services;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API;

/// <summary>
/// Runs at startup and every 6 hours:
///  1. Monthly usage snapshot + reset (1st of the month)
///  2. Lifecycle: Trial -> TrialExpired; paid -> GracePeriod (0-3 days overdue) -> Suspended
///  3. Reminder emails to owners (each kind once per renewal date) + one daily Telegram digest for the admin
/// Agency sub-tenants are skipped: they follow their agency.
/// </summary>
public class BillingLifecycleWorker : BackgroundService
{
    private const int GraceDays = 3;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BillingLifecycleWorker> _logger;

    public BillingLifecycleWorker(IServiceProvider serviceProvider, ILogger<BillingLifecycleWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BillingLifecycleWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                await ProcessMonthlyResetsAsync(db, stoppingToken);
                await EnforceLifecycleAsync(db, stoppingToken);
                await SendRemindersAsync(db, email, config, stoppingToken);
                await SendAdminDigestAsync(db, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during BillingLifecycleWorker execution cycle.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    // ---------------- 1. monthly reset ----------------

    private async Task ProcessMonthlyResetsAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // The snapshot taken on the 1st holds the PREVIOUS month's usage, so label it with that month.
        var cycle = now.AddMonths(-1).ToString("yyyy-MM");

        if (now.Day != 1 || await db.UsageLedgers.AnyAsync(l => l.BillingCycle == cycle, ct)) return;

        _logger.LogInformation("Monthly usage snapshot and reset for cycle {Cycle}.", cycle);

        foreach (var t in await db.Tenants.ToListAsync(ct))
        {
            db.UsageLedgers.Add(new UsageLedger
            {
                TenantId = t.Id, AgencyId = t.AgencyId, BillingCycle = cycle,
                StandardTasksUsed = t.TasksUsedThisMonth, AiTasksUsed = t.AiTasksUsedThisMonth, RecordedAt = now
            });
            t.TasksUsedThisMonth = 0;
            t.AiTasksUsedThisMonth = 0;
        }

        foreach (var a in await db.Agencies.ToListAsync(ct))
        {
            a.TasksUsedThisMonth = 0;
            a.AiTasksUsedThisMonth = 0;
        }

        await db.SaveChangesAsync(ct);
    }

    // ---------------- 2. lifecycle ----------------

    private async Task EnforceLifecycleAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var tenants = await db.Tenants
            .Where(t => t.AgencyId == null && t.NextRenewalDate < now &&
                        t.BillingStatus != "Suspended" && t.BillingStatus != "TrialExpired")
            .ToListAsync(ct);
        foreach (var t in tenants) t.BillingStatus = NextStatus(t.BillingStatus, t.NextRenewalDate, now);

        var agencies = await db.Agencies
            .Where(a => a.NextRenewalDate < now && a.BillingStatus != "Suspended" && a.BillingStatus != "TrialExpired")
            .ToListAsync(ct);
        foreach (var a in agencies) a.BillingStatus = NextStatus(a.BillingStatus, a.NextRenewalDate, now);

        if (tenants.Count > 0 || agencies.Count > 0) await db.SaveChangesAsync(ct);
    }

    private static string NextStatus(string current, DateTime renewal, DateTime now)
    {
        if (current == "Trial") return "TrialExpired";            // trials end without a grace period
        if (current == "PendingPayment") return "Suspended";      // never paid
        return (now - renewal).TotalDays > GraceDays ? "Suspended" : "GracePeriod";
    }

    // ---------------- 3. reminders ----------------

    private record Account(string Type, Guid Id, string Name, string Email, string Status, DateTime Renewal, string Plan);

    private async Task SendRemindersAsync(AppDbContext db, IEmailSender email, IConfiguration config, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(8);
        var appUrl = (config["App:FrontendUrl"] ?? "https://omni.mohammad-abujalboush.com").TrimEnd('/');

        var accounts = new List<Account>();
        accounts.AddRange(await db.Tenants.AsNoTracking()
            .Where(t => t.AgencyId == null && t.NextRenewalDate < horizon)
            .Select(t => new Account("Tenant", t.Id, t.Name, t.OwnerEmail, t.BillingStatus, t.NextRenewalDate, t.SubscriptionTier))
            .ToListAsync(ct));
        accounts.AddRange(await db.Agencies.AsNoTracking()
            .Where(a => a.NextRenewalDate < horizon)
            .Select(a => new Account("Agency", a.Id, a.Name, a.OwnerEmail, a.BillingStatus, a.NextRenewalDate, a.SubscriptionTier))
            .ToListAsync(ct));

        foreach (var acc in accounts)
        {
            var kind = ReminderKind(acc, now);
            if (kind == null || string.IsNullOrWhiteSpace(acc.Email)) continue;

            var cycleKey = acc.Renewal.ToString("yyyy-MM-dd");
            if (await db.ReminderLogs.AnyAsync(r => r.AccountId == acc.Id && r.Kind == kind && r.CycleKey == cycleKey, ct))
                continue;

            var (subject, body) = ReminderText(kind, acc, appUrl);
            await email.SendAsync(acc.Email, subject, body);

            db.ReminderLogs.Add(new ReminderLog
            {
                AccountType = acc.Type, AccountId = acc.Id, Kind = kind, CycleKey = cycleKey, SentAt = now
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private static string? ReminderKind(Account a, DateTime now)
    {
        var daysLeft = (a.Renewal - now).TotalDays;

        if (a.Status == "Trial")
            return daysLeft <= 1 ? "trial_1" : daysLeft <= 3 ? "trial_3" : null;
        if (a.Status == "TrialExpired") return "trial_expired";
        if (a.Status == "Suspended") return "suspended";
        if (a.Status == "GracePeriod") return "overdue";
        if (a.Status is "Active")
            return daysLeft <= 1 ? "due_1" : daysLeft <= 3 ? "due_3" : daysLeft <= 7 ? "due_7" : null;
        return null; // Paused, PendingPayment
    }

    private static (string Subject, string Body) ReminderText(string kind, Account a, string appUrl)
    {
        var date = a.Renewal.ToString("d MMM yyyy");
        var billing = $"{appUrl}/#/billing";
        var name = System.Net.WebUtility.HtmlEncode(a.Name);
        string Btn(string text) => $"<p><a href=\"{billing}\" style=\"background:#2457c5;color:#fff;padding:10px 16px;border-radius:6px;text-decoration:none\">{text}</a></p>";

        return kind switch
        {
            "trial_3" => ("Your OmniNode trial ends in 3 days",
                $"<p>Hi {name},</p><p>Your free trial ends on <b>{date}</b>. Choose a plan to keep your integrations running without interruption.</p>{Btn("Choose a plan")}"),
            "trial_1" => ("Your OmniNode trial ends tomorrow",
                $"<p>Hi {name},</p><p>Your free trial ends on <b>{date}</b>. After that, incoming webhooks stop until a plan is active. Your setup is kept.</p>{Btn("Choose a plan")}"),
            "trial_expired" => ("Your OmniNode trial has ended",
                $"<p>Hi {name},</p><p>Your free trial has ended and webhooks are paused. Everything you built is saved; pick a plan to switch it back on.</p>{Btn("Choose a plan")}"),
            "due_7" => ("Your OmniNode plan renews in 7 days",
                $"<p>Hi {name},</p><p>Your <b>{a.Plan}</b> plan renews on <b>{date}</b>. Generate your invoice now so there's time for the transfer to clear.</p>{Btn("Renew now")}"),
            "due_3" => ("Your OmniNode plan renews in 3 days",
                $"<p>Hi {name},</p><p>Your <b>{a.Plan}</b> plan renews on <b>{date}</b>.</p>{Btn("Renew now")}"),
            "due_1" => ("Your OmniNode plan renews tomorrow",
                $"<p>Hi {name},</p><p>Your <b>{a.Plan}</b> plan renews on <b>{date}</b>. If you've already paid, you can ignore this email.</p>{Btn("Renew now")}"),
            "overdue" => ("Payment overdue: your OmniNode account will be paused soon",
                $"<p>Hi {name},</p><p>Your plan renewal was due on <b>{date}</b>. Webhooks keep running for {GraceDays} days after the due date, then the account is suspended.</p>{Btn("Pay now")}"),
            _ => ("Your OmniNode account is suspended",
                $"<p>Hi {name},</p><p>Your account is suspended because the renewal due on <b>{date}</b> wasn't paid. Webhooks are paused; your setup is kept.</p>{Btn("Pay now")}")
        };
    }

    // ---------------- 4. admin digest ----------------

    private async Task SendAdminDigestAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = now.ToString("yyyy-MM-dd");
        if (now.Hour < 5) return; // send after ~08:00 Amman
        if (await db.ReminderLogs.AnyAsync(r => r.AccountId == Guid.Empty && r.Kind == "digest" && r.CycleKey == today, ct)) return;

        var soon = now.AddDays(7);
        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => t.AgencyId == null && t.NextRenewalDate < soon && t.BillingStatus != "Paused")
            .Select(t => new { Type = "Merchant", t.Name, t.BillingStatus, t.NextRenewalDate, t.SubscriptionTier })
            .ToListAsync(ct);
        var agencies = await db.Agencies.AsNoTracking()
            .Where(a => a.NextRenewalDate < soon)
            .Select(a => new { Type = "Agency", a.Name, a.BillingStatus, a.NextRenewalDate, a.SubscriptionTier })
            .ToListAsync(ct);
        var reported = await db.PaymentInvoices.AsNoTracking()
            .Where(i => i.Status == "Unpaid" && i.PaymentReportedAt != null)
            .Select(i => new { i.ReferenceCode, i.Amount, i.Currency })
            .ToListAsync(ct);

        var rows = tenants.Concat(agencies).OrderBy(r => r.NextRenewalDate).ToList();
        if (rows.Count > 0 || reported.Count > 0)
        {
            var sb = new StringBuilder("📋 Daily billing digest\n");
            if (reported.Count > 0)
            {
                sb.Append($"\n💳 Payments reported, waiting for approval ({reported.Count}):\n");
                foreach (var r in reported.Take(15)) sb.Append($"• {r.ReferenceCode}: {r.Amount:0.###} {r.Currency}\n");
            }
            foreach (var group in rows.GroupBy(r => r.BillingStatus))
            {
                sb.Append($"\n{group.Key} ({group.Count()}):\n");
                foreach (var r in group.Take(15))
                    sb.Append($"• {r.Type}: {r.Name} ({r.SubscriptionTier}), {r.NextRenewalDate:d MMM}\n");
            }
            TelegramAlert.Fire(sb.ToString());
        }

        db.ReminderLogs.Add(new ReminderLog { AccountType = "System", AccountId = Guid.Empty, Kind = "digest", CycleKey = today, SentAt = now });
        await db.SaveChangesAsync(ct);
    }
}
