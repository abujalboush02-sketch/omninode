using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API;

public class BillingLifecycleWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BillingLifecycleWorker> _logger;

    public BillingLifecycleWorker(IServiceProvider serviceProvider, ILogger<BillingLifecycleWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BillingLifecycleWorker started. Monitoring account lifecycles and quotas...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                await ProcessMonthlyResetsAsync(dbContext, stoppingToken);
                await EnforceAccountRenewalsAsync(dbContext, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during BillingLifecycleWorker execution cycle.");
            }

            // Check every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task ProcessMonthlyResetsAsync(AppDbContext dbContext, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentCycle = now.ToString("yyyy-MM");

        // Verify if a snapshot for the current cycle has already been created
        var alreadyArchived = await dbContext.UsageLedgers
            .AnyAsync(l => l.BillingCycle == currentCycle, ct);

        // Run archiving on the 1st of the month if not already executed
        if (now.Day == 1 && !alreadyArchived)
        {
            _logger.LogInformation("Initiating automated monthly quota reset and ledger archiving for cycle {Cycle}...", currentCycle);

            var tenants = await dbContext.Tenants.ToListAsync(ct);
            foreach (var t in tenants)
            {
                dbContext.UsageLedgers.Add(new UsageLedger
                {
                    TenantId = t.Id,
                    AgencyId = t.AgencyId,
                    BillingCycle = currentCycle,
                    StandardTasksUsed = t.TasksUsedThisMonth,
                    AiTasksUsed = t.AiTasksUsedThisMonth,
                    OverageIncurred = 0,
                    RecordedAt = now
                });

                t.TasksUsedThisMonth = 0;
                t.AiTasksUsedThisMonth = 0;
            }

            var agencies = await dbContext.Agencies.ToListAsync(ct);
            foreach (var a in agencies)
            {
                a.TasksUsedThisMonth = 0;
                a.AiTasksUsedThisMonth = 0;
            }

            await dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Monthly quota reset and usage snapshot successfully saved.");
        }
    }

    private async Task EnforceAccountRenewalsAsync(AppDbContext dbContext, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // 1. Process Expired Tenants
        var expiredTenants = await dbContext.Tenants
            .Where(t => t.NextRenewalDate < now && t.BillingStatus != "Suspended")
            .ToListAsync(ct);

        foreach (var tenant in expiredTenants)
        {
            var daysOverdue = (now - tenant.NextRenewalDate).TotalDays;

            if (daysOverdue > 3)
            {
                tenant.BillingStatus = "Suspended";
                _logger.LogWarning("Tenant {TenantId} has been Suspended due to unpaid renewal past grace period.", tenant.Id);
            }
            else
            {
                tenant.BillingStatus = "GracePeriod";
            }
        }

        // 2. Process Expired Agencies
        var expiredAgencies = await dbContext.Agencies
            .Where(a => a.NextRenewalDate < now && a.BillingStatus != "Suspended")
            .ToListAsync(ct);

        foreach (var agency in expiredAgencies)
        {
            var daysOverdue = (now - agency.NextRenewalDate).TotalDays;

            if (daysOverdue > 3)
            {
                agency.BillingStatus = "Suspended";
                _logger.LogWarning("Agency {AgencyId} has been Suspended due to unpaid renewal past grace period.", agency.Id);
            }
            else
            {
                agency.BillingStatus = "GracePeriod";
            }
        }

        if (expiredTenants.Any() || expiredAgencies.Any())
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }
}