# OmniNode Middleware API

**OmniNode** is an enterprise-grade, multi-tenant middleware API designed to act as the universal bridge between e-commerce source platforms (like Shopify and WooCommerce) and destination CRMs (like HubSpot and Salesforce).

Built on a robust .NET 10 architecture and utilizing PostgreSQL with an advanced outbox pattern, OmniNode guarantees zero data loss during high-volume webhook bursts. It features native AI parsing via Groq's Llama 3 models, allowing unstructured data from channels like WhatsApp to be automatically structured and routed seamlessly.

---

## 🌍 Live Deployment & Documentation

* **Live Endpoint:** `https://omni.mohammad-abujalboush.com/api`
* **Infrastructure:** Containerized via Docker Compose, hosted on a Contabo VPS, and routed securely through an Nginx reverse proxy.
* **Authentication:** All requests must include the `X-Api-Key` header.
* **Interactive Documentation:** A complete OpenAPI Swagger specification is available at `/docs` on the live domain, providing interactive testing and code snippets.

---

## 🏗 Core Architecture & Capabilities

* **Strict Multi-Tenancy:** Cryptographically hashed API keys and execution-context tenant scoping prevent cross-tenant data leaks (IDOR). A master key secures administrative billing and provisioning endpoints.
* **Transactional Outbox Pattern:** Incoming webhooks are immediately persisted to a PostgreSQL `jsonb` outbox. An adaptive, parallelized background worker (`EventProcessingWorker`) processes the queue, guaranteeing delivery even if the destination CRM experiences downtime.
* **Resilient Execution Engine:** Built with Entity Framework Core execution strategies, transient fault handling, and exponential backoff.
* **AI Unstructured Parsing:** Integrates with Groq (Llama 3 8B) with strict JSON-mode compliance and prompt-injection safeguards to convert messy chat logs into actionable CRM schemas.
* **Deep JSON Transformation:** A custom dot-notation mapping engine flattens complex e-commerce payloads and applies data type casting and mathematical conversions (e.g., converting cents to dollars) on the fly.

---

## 🚀 Quick Start (Docker Deployment)

OmniNode is designed for rapid deployment via Docker Compose.

1. **Configure Environment Variables**  
   Create a `.env` file in the root directory:
   ```env
   GROQ_API_KEY=your_groq_api_key
   DB_PASSWORD=your_secure_postgres_password
   MASTER_API_KEY=um_master_secret_key_123