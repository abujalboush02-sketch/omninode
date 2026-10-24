-- Run ONCE after the new API has started (migration "TrialsReportsReminders" applied on startup).
-- docker exec -i omninode_db psql -U postgres -d omninode < sql/pass3_backfill.sql
BEGIN;

-- Standalone Free/Starter tenants become 14-day trials (150 tasks, 20 AI)
UPDATE "Tenants"
SET "SubscriptionTier" = 'Free Trial', "MonthlyTaskQuota" = 150, "AiTasksQuota" = 20,
    "BillingStatus" = 'Trial', "NextRenewalDate" = now() + interval '14 days'
WHERE "AgencyId" IS NULL AND "SubscriptionTier" IN ('Free', 'Starter', 'Free Trial')
  AND "BillingStatus" IN ('Active', 'GracePeriod', 'Suspended');

-- Agencies that never paid become 14-day Agency Trials (2 clients, 1,000 tasks, 50 AI)
UPDATE "Agencies"
SET "SubscriptionTier" = 'Agency Trial', "MonthlyTaskQuota" = 1000, "MonthlyAiQuota" = 50, "MaxSubTenants" = 2,
    "BillingStatus" = 'Trial', "NextRenewalDate" = now() + interval '14 days'
WHERE "BillingStatus" = 'PendingPayment'
  AND NOT EXISTS (SELECT 1 FROM "PaymentInvoices" p WHERE p."AccountId" = "Agencies"."Id" AND p."Status" = 'Confirmed');

-- Mark existing unstructured (AI) events, so AI analytics include history
UPDATE "RawEvents" SET "IsAi" = true
WHERE "Payload" ? 'userId' AND "Payload" ? 'payload' AND "IsAi" = false;

COMMIT;
