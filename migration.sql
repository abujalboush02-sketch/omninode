CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "Connections" (
    "Id" uuid NOT NULL,
    "SourceEndpointId" uuid NOT NULL,
    "DestinationEndpointId" uuid NOT NULL,
    "Status" text NOT NULL,
    CONSTRAINT "PK_Connections" PRIMARY KEY ("Id")
);

CREATE TABLE "ConversationSessions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ExternalUserId" text NOT NULL,
    "Status" text NOT NULL,
    CONSTRAINT "PK_ConversationSessions" PRIMARY KEY ("Id")
);

CREATE TABLE "Endpoints" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PlatformName" text NOT NULL,
    "EndpointType" text NOT NULL,
    "WebhookUrl" text NOT NULL,
    "AuthHeaders" text NOT NULL,
    CONSTRAINT "PK_Endpoints" PRIMARY KEY ("Id")
);

CREATE TABLE "IncomingEvents" (
    "Id" uuid NOT NULL,
    "RawEventId" uuid NOT NULL,
    "ConnectionId" uuid NOT NULL,
    "Payload" text NOT NULL,
    "Status" text NOT NULL,
    CONSTRAINT "PK_IncomingEvents" PRIMARY KEY ("Id")
);

CREATE TABLE "Mappings" (
    "Id" uuid NOT NULL,
    "ConnectionId" uuid NOT NULL,
    "SourceSchema" text NOT NULL,
    "DestinationSchema" text NOT NULL,
    "FieldMappings" text NOT NULL,
    CONSTRAINT "PK_Mappings" PRIMARY KEY ("Id")
);

CREATE TABLE "OrderDrafts" (
    "Id" uuid NOT NULL,
    "SessionId" uuid NOT NULL,
    "CustomerName" text NOT NULL,
    "Product" text NOT NULL,
    "PhoneNumber" text NOT NULL,
    "Location" text NOT NULL,
    "Status" text NOT NULL,
    CONSTRAINT "PK_OrderDrafts" PRIMARY KEY ("Id")
);

CREATE TABLE "RawEvents" (
    "Id" uuid NOT NULL,
    "EndpointId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Payload" text NOT NULL,
    "ReceivedAt" timestamp with time zone NOT NULL,
    "Status" text NOT NULL,
    "RetryCount" integer NOT NULL,
    "MaxRetries" integer NOT NULL,
    "LastAttemptAt" timestamp with time zone,
    "FailureReason" text NOT NULL,
    CONSTRAINT "PK_RawEvents" PRIMARY KEY ("Id")
);

CREATE TABLE "SchemaTemplates" (
    "Id" uuid NOT NULL,
    "PlatformName" text NOT NULL,
    "SchemaJson" text NOT NULL,
    CONSTRAINT "PK_SchemaTemplates" PRIMARY KEY ("Id")
);

CREATE TABLE "TenantCredentials" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "BaseUrl" text NOT NULL,
    "ApiKey" text NOT NULL,
    "EndpointId" uuid NOT NULL,
    "AuthType" text NOT NULL,
    CONSTRAINT "PK_TenantCredentials" PRIMARY KEY ("Id")
);

CREATE TABLE "Tenants" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "ApiKeyHash" text NOT NULL,
    "OwnerName" text NOT NULL,
    "OwnerEmail" text NOT NULL,
    "PlanType" text NOT NULL,
    "SubscriptionTier" text NOT NULL,
    "MonthlyTaskQuota" integer NOT NULL,
    "TasksUsedThisMonth" integer NOT NULL,
    CONSTRAINT "PK_Tenants" PRIMARY KEY ("Id")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260424184726_AddTenantBillingIdentity', '9.0.0');

ALTER TABLE "Tenants" ADD "BillingProvider" text;

ALTER TABLE "Tenants" ADD "ExternalBillingReference" text;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260424184928_AddExternalBillingReferences', '9.0.0');

ALTER TABLE "Tenants" ALTER COLUMN "Name" TYPE character varying(255);

ALTER TABLE "Tenants" ADD "AgencyId" uuid;

ALTER TABLE "Tenants" ADD "AiTasksQuota" integer NOT NULL DEFAULT 0;

ALTER TABLE "Tenants" ADD "AiTasksUsedThisMonth" integer NOT NULL DEFAULT 0;

ALTER TABLE "Tenants" ADD "BillingStatus" text NOT NULL DEFAULT '';

ALTER TABLE "Tenants" ADD "NextRenewalDate" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '-infinity';

ALTER TABLE "Tenants" ADD "TenantType" text NOT NULL DEFAULT '';

ALTER TABLE "SchemaTemplates" ALTER COLUMN "SchemaJson" TYPE jsonb;

ALTER TABLE "RawEvents" ALTER COLUMN "Status" TYPE character varying(50);

ALTER TABLE "RawEvents" ALTER COLUMN "Payload" TYPE jsonb;

ALTER TABLE "Mappings" ALTER COLUMN "SourceSchema" TYPE jsonb;

ALTER TABLE "Mappings" ALTER COLUMN "FieldMappings" TYPE jsonb;

ALTER TABLE "Mappings" ALTER COLUMN "DestinationSchema" TYPE jsonb;

ALTER TABLE "IncomingEvents" ALTER COLUMN "Payload" TYPE jsonb;

ALTER TABLE "Connections" ADD "ApiKey" text NOT NULL DEFAULT '';

ALTER TABLE "Connections" ADD "AuthHeaderName" text;

ALTER TABLE "Connections" ADD "AuthType" text NOT NULL DEFAULT '';

ALTER TABLE "Connections" ADD "BaseUrl" text NOT NULL DEFAULT '';

CREATE TABLE "Agencies" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "OwnerEmail" text NOT NULL,
    "SubscriptionTier" text NOT NULL,
    "MaxSubTenants" integer NOT NULL,
    "MonthlyTaskQuota" integer NOT NULL,
    "MonthlyAiQuota" integer NOT NULL,
    "TasksUsedThisMonth" integer NOT NULL,
    "AiTasksUsedThisMonth" integer NOT NULL,
    "BillingStatus" text NOT NULL,
    "NextRenewalDate" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Agencies" PRIMARY KEY ("Id")
);

CREATE TABLE "FieldMappingRules" (
    "Id" uuid NOT NULL,
    "EndpointId" uuid NOT NULL,
    "SourceKey" text NOT NULL,
    "TargetKey" text NOT NULL,
    "Type" text NOT NULL,
    "Math" text NOT NULL,
    "IsRequired" boolean NOT NULL,
    CONSTRAINT "PK_FieldMappingRules" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_FieldMappingRules_Endpoints_EndpointId" FOREIGN KEY ("EndpointId") REFERENCES "Endpoints" ("Id") ON DELETE CASCADE
);

CREATE TABLE "PaymentInvoices" (
    "Id" uuid NOT NULL,
    "AccountType" text NOT NULL,
    "AccountId" uuid NOT NULL,
    "Amount" numeric NOT NULL,
    "Currency" text NOT NULL,
    "PaymentMethod" text NOT NULL,
    "ReferenceCode" text NOT NULL,
    "Status" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "PaidAt" timestamp with time zone,
    "ConfirmedByAdminId" uuid,
    CONSTRAINT "PK_PaymentInvoices" PRIMARY KEY ("Id")
);

CREATE TABLE "UsageLedgers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "AgencyId" uuid,
    "BillingCycle" text NOT NULL,
    "StandardTasksUsed" integer NOT NULL,
    "AiTasksUsed" integer NOT NULL,
    "OverageIncurred" numeric NOT NULL,
    "RecordedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_UsageLedgers" PRIMARY KEY ("Id")
);

CREATE TABLE "Users" (
    "Id" uuid NOT NULL,
    "Email" text NOT NULL,
    "PasswordHash" text NOT NULL,
    "Role" integer NOT NULL,
    "AgencyId" uuid,
    "TenantId" uuid,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastLoginAt" timestamp with time zone,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Users_Agencies_AgencyId" FOREIGN KEY ("AgencyId") REFERENCES "Agencies" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Users_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE SET NULL
);

CREATE INDEX "IX_Tenants_AgencyId" ON "Tenants" ("AgencyId");

CREATE UNIQUE INDEX "IX_Tenants_ApiKeyHash" ON "Tenants" ("ApiKeyHash");

CREATE INDEX "IX_TenantCredentials_EndpointId" ON "TenantCredentials" ("EndpointId");

CREATE INDEX "IX_TenantCredentials_TenantId" ON "TenantCredentials" ("TenantId");

CREATE INDEX "IX_RawEvent_WorkerQueue" ON "RawEvents" ("Status", "LastAttemptAt", "ReceivedAt");

CREATE INDEX "IX_RawEvents_EndpointId" ON "RawEvents" ("EndpointId");

CREATE INDEX "IX_Endpoints_TenantId_Id" ON "Endpoints" ("TenantId", "Id");

CREATE UNIQUE INDEX "IX_Connections_SourceEndpointId" ON "Connections" ("SourceEndpointId");

CREATE INDEX "IX_FieldMappingRules_EndpointId" ON "FieldMappingRules" ("EndpointId");

CREATE UNIQUE INDEX "IX_PaymentInvoices_ReferenceCode" ON "PaymentInvoices" ("ReferenceCode");

CREATE INDEX "IX_Users_AgencyId" ON "Users" ("AgencyId");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

CREATE INDEX "IX_Users_TenantId" ON "Users" ("TenantId");

ALTER TABLE "Connections" ADD CONSTRAINT "FK_Connections_Endpoints_SourceEndpointId" FOREIGN KEY ("SourceEndpointId") REFERENCES "Endpoints" ("Id") ON DELETE CASCADE;

ALTER TABLE "Endpoints" ADD CONSTRAINT "FK_Endpoints_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE RESTRICT;

ALTER TABLE "RawEvents" ADD CONSTRAINT "FK_RawEvents_Endpoints_EndpointId" FOREIGN KEY ("EndpointId") REFERENCES "Endpoints" ("Id") ON DELETE CASCADE;

ALTER TABLE "TenantCredentials" ADD CONSTRAINT "FK_TenantCredentials_Endpoints_EndpointId" FOREIGN KEY ("EndpointId") REFERENCES "Endpoints" ("Id") ON DELETE CASCADE;

ALTER TABLE "TenantCredentials" ADD CONSTRAINT "FK_TenantCredentials_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE;

ALTER TABLE "Tenants" ADD CONSTRAINT "FK_Tenants_Agencies_AgencyId" FOREIGN KEY ("AgencyId") REFERENCES "Agencies" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261002182435_AddAgencyAndUserHierarchy', '9.0.0');

COMMIT;

