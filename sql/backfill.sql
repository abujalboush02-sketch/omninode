-- Run ONCE, AFTER the new API has started (it applies the EF migration on startup).
-- docker compose exec -T <postgres-service> psql -U <user> -d <db> < sql/backfill.sql
BEGIN;

-- 1. Existing source endpoints get a secret inbound URL (needed for /api/Webhooks/s/{secret})
UPDATE "Endpoints"
SET "WebhookSecret" = 'whs_' || replace(replace(rtrim(encode(gen_random_bytes(24), 'base64'), '='), '+', '-'), '/', '_')
WHERE lower("EndpointType") = 'source' AND "WebhookSecret" IS NULL;

-- 2. Normalise endpoint types to lowercase
UPDATE "Endpoints" SET "EndpointType" = lower("EndpointType")
WHERE "EndpointType" <> lower("EndpointType");

-- 3. Legacy tier name "Starter" -> Free plan (150 tasks, 0 AI). Standalone tenants only.
UPDATE "Tenants"
SET "SubscriptionTier" = 'Free', "MonthlyTaskQuota" = 150, "AiTasksQuota" = 0
WHERE "SubscriptionTier" = 'Starter' AND "AgencyId" IS NULL;

-- 4. Free tenants that the old worker suspended or put in grace: reactivate
UPDATE "Tenants"
SET "BillingStatus" = 'Active', "NextRenewalDate" = now() + interval '30 days'
WHERE "SubscriptionTier" = 'Free' AND "AgencyId" IS NULL
  AND "BillingStatus" IN ('Suspended', 'GracePeriod');

-- 5. Tenants still holding an empty key hash get a random (unusable) one; they rotate from Settings
UPDATE "Tenants" SET "ApiKeyHash" = gen_random_uuid()::text
WHERE "ApiKeyHash" = '' OR "ApiKeyHash" IS NULL;

-- 6. Connection status: legacy "Created" -> MappingRequired, or Active if a mapping exists
UPDATE "Connections" c SET "Status" = 'Active'
WHERE c."Status" IN ('Created', 'MappingRequired')
  AND EXISTS (SELECT 1 FROM "Mappings" m WHERE m."ConnectionId" = c."Id");
UPDATE "Connections" SET "Status" = 'MappingRequired' WHERE "Status" = 'Created';

-- 7. New timestamp columns were added with a 0001-01-01 default
UPDATE "Connections" SET "CreatedAt" = now() WHERE "CreatedAt" < '2000-01-01';
UPDATE "Mappings"    SET "UpdatedAt" = now() WHERE "UpdatedAt" < '2000-01-01';

-- 8. Events stuck in Processing by the old null-FailureReason bug
UPDATE "RawEvents" SET "Status" = 'Pending', "FailureReason" = ''
WHERE "Status" = 'Processing';
UPDATE "RawEvents" SET "FailureReason" = '' WHERE "FailureReason" IS NULL;

COMMIT;
