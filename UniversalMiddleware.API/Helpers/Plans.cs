using System;
using System.Collections.Generic;
using System.Linq;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.API.Helpers;

/// <summary>
/// Single-tenant plan catalog. Must stay in sync with the frontend's lib/core/config/plans.dart.
/// </summary>
public record PlanDef(string Id, string Name, decimal PriceJod, decimal PriceUsdRef, int Tasks, int AiTasks, string[] Extras);

public static class Plans
{
    public static readonly IReadOnlyList<PlanDef> All = new List<PlanDef>
    {
        new("free",     "Free",     0m,   0m,   150,     0,     new[] { "Community support" }),
        new("bronze",   "Bronze",   14m,  19m,  2_500,   0,     new[] { "Email support" }),
        new("silver",   "Silver",   35m,  49m,  10_000,  250,   new[] { "Email support", "Most popular" }),
        new("gold",     "Gold",     85m,  119m, 35_000,  1_000, new[] { "Priority email support" }),
        new("platinum", "Platinum", 179m, 249m, 100_000, 5_000, new[] { "Priority support", "Onboarding call" }),
    };

    /// <summary>Accepts a plan id or tier name, case-insensitive. Legacy "Starter" maps to Free.</summary>
    public static PlanDef? Find(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        var key = idOrName.Trim();
        if (key.Equals("starter", StringComparison.OrdinalIgnoreCase)) key = "free";
        return All.FirstOrDefault(p =>
            p.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsFree(Tenant tenant) => Find(tenant.SubscriptionTier)?.Id == "free";

    public static void Apply(Tenant tenant, PlanDef plan)
    {
        tenant.SubscriptionTier = plan.Name;
        tenant.MonthlyTaskQuota = plan.Tasks;
        tenant.AiTasksQuota = plan.AiTasks;
    }

    // Webhooks are accepted up to this share of the quota before 429.
    // Applies to both the client's own allocation and the agency pool.
    public const int GraceNumerator = 11;   // 110%
    public const int GraceDenominator = 10;
}

/// <summary>
/// Agency plan catalog. Prices are the exact JOD conversion at the 0.709 peg; USD kept as reference.
/// Platinum limits are defaults; SuperAdmin can set custom limits per agency.
/// </summary>
public record AgencyPlanDef(string Id, string Name, decimal PriceJod, decimal PriceUsdRef, int MaxClients,
    int Tasks, int AiTasks, bool WhiteLabelIncluded, bool CustomPricing, string[] Extras);

public static class AgencyPlans
{
    public const decimal WhiteLabelAddOnJod = 20.561m;   // $29
    public const decimal WhiteLabelAddOnUsdRef = 29m;

    public static readonly IReadOnlyList<AgencyPlanDef> All = new List<AgencyPlanDef>
    {
        new("agency_bronze",   "Agency Bronze",   70.191m,  99m,  5,   30_000,  1_000,  false, false,
            new[] { "Multi-client management", "Pooled task quota" }),
        new("agency_silver",   "Agency Silver",   176.541m, 249m, 15,  80_000,  5_000,  false, false,
            new[] { "Multi-client management", "Higher volume" }),
        new("agency_gold",     "Agency Gold",     353.791m, 499m, 40,  250_000, 15_000, true,  false,
            new[] { "White-label client reports", "Priority support", "90-day logs" }),
        new("agency_platinum", "Agency Platinum", 637.391m, 899m, 200, 750_000, 50_000, true,  true,
            new[] { "White-label client reports", "SLA", "Priority support", "Custom limits" }),
    };

    /// <summary>Accepts id or tier name, case-insensitive. Legacy "Agency Bronze" tier name maps to agency_bronze.</summary>
    public static AgencyPlanDef? Find(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        var key = idOrName.Trim();
        return All.FirstOrDefault(p =>
            p.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public static void Apply(Agency agency, AgencyPlanDef plan)
    {
        agency.SubscriptionTier = plan.Name;
        agency.MaxSubTenants = plan.MaxClients;
        agency.MonthlyTaskQuota = plan.Tasks;
        agency.MonthlyAiQuota = plan.AiTasks;
        if (plan.WhiteLabelIncluded) agency.WhiteLabel = true;
    }
}
