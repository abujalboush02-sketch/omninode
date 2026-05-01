# OmniNode Universal Middleware

A production-ready, multi-tenant middleware designed to bridge any Source Marketplace (Shopify, WooCommerce) or Unstructured Source (WhatsApp via Llama 3 AI) with any Destination CRM (HubSpot, Salesforce).

## Live API & Documentation
* **Base URL:** `https://omni.mohammad-abujalboush.com/api`
* **Authentication:** Pass your API key via the `X-Api-Key` header.
* **OpenAPI Spec:** A complete interactive Swagger UI and code snippets in 15 languages are provided via our APILayer portal.

## Pricing
We offer flexible, usage-based tiers for developers and enterprises via APILayer:
* **Free Tier:** $0/month (100 API Requests) - Perfect for testing the AI text-to-JSON parser.
* **Starter Tier:** $29/month (1,000 API Requests) - For early-stage SaaS apps.
* **Pro Tier:** $99/month (10,000 API Requests) - Production-ready limits.

## Architecture Highlights
- **Multi-Tenant:** Secure and isolated tenant execution environments.
- **Llama 3 AI-Driven Unstructured Parsing:** Passively ingest messages from WhatsApp/Telegram to parse orders automatically.
- **n8n-friendly Webhook Architecture:** Robust ingestion engine that easily integrates with standard webhook platforms.
- **PostgreSQL Outbox Pattern:** High-reliability background processing with exponential backoff retries. If the destination CRM fails, the system automatically retries without data loss.

## Step-by-Step API Workflow
Follow this exact workflow to configure a new integration via the Management Shell:

1. **Create Tenant** (`POST /api/Tenants`): Register the new client and secure an API key.
2. **Create Endpoints** (`POST /api/Endpoints`): Register the Source Endpoint (e.g. Shopify webhook) and the Destination Endpoint (e.g. HubSpot CRM).
3. **Add Credentials** (`POST /api/Credentials`): Securely store API keys or tokens required for the generic output adapter to POST to the destination.
4. **Create Connection** (`POST /api/Connections`): Link the Source Endpoint to the Destination Endpoint.
5. **Configure Mappings** (`POST /api/Mappings/configure`): Set up the `FieldMappingRules` to map incoming fields (e.g. `order_total`) to destination fields (e.g. `amount_due`), including math and type transformations. This makes the connection `Active`.
6. **Fire Webhooks**: Webhooks can now be ingested via `POST /api/webhooks/structured/{endpointId}` or `POST /api/webhooks/unstructured/{endpointId}?userId={phone}`. They are safely buffered in the PostgreSQL Outbox.

## Logs & Monitoring
Monitor the health and queues using:
- `GET /api/Events/logs/{tenantId}`: Get recent events for a specific tenant.
- `GET /api/Events/pending`: Monitor the outbound queue.
- `GET /api/Events/failed`: Review events that exhausted all retries.
