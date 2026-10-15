-- Run ONCE after the new API has started (it applies the AgencyPass migration on startup).
-- docker exec -i omninode_db psql -U postgres -d omninode < sql/agency_backfill.sql
BEGIN;

-- Existing Agency Bronze accounts get the new Bronze limits (30,000 tasks, 1,000 AI, 5 clients)
UPDATE "Agencies" SET "MonthlyTaskQuota" = 30000, "MonthlyAiQuota" = 1000, "MaxSubTenants" = 5
WHERE "SubscriptionTier" IN ('Agency Bronze', 'Bronze');
UPDATE "Agencies" SET "SubscriptionTier" = 'Agency Bronze' WHERE "SubscriptionTier" = 'Bronze';

-- Report the agencies whose client allocations now exceed their pool (fix from the portal)
SELECT a."Name", a."MonthlyTaskQuota", SUM(t."MonthlyTaskQuota") AS allocated
FROM "Agencies" a JOIN "Tenants" t ON t."AgencyId" = a."Id"
GROUP BY a."Id", a."Name", a."MonthlyTaskQuota"
HAVING SUM(t."MonthlyTaskQuota") > a."MonthlyTaskQuota";

-- The old monthly snapshot was labelled with the NEW month; shift existing labels back one month
UPDATE "UsageLedgers"
SET "BillingCycle" = to_char(to_date("BillingCycle" || '-01', 'YYYY-MM-DD') - interval '1 month', 'YYYY-MM')
WHERE "RecordedAt" < now();

COMMIT;
