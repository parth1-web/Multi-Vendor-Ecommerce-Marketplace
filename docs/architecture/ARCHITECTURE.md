# System Architecture

> Multi-Vendor E-Commerce Marketplace — Clean Architecture modular monolith with a Next.js App Router frontend.

## 1. Architectural drivers

The dominant constraint of a marketplace is **correctness of money and stock**, not raw throughput. Every architectural decision below is subordinate to four invariants:

| # | Invariant | Consequence |
| --- | --- | --- |
| I1 | A customer can never buy the same unit of stock twice | Transactional inventory reservation + row-level concurrency |
| I2 | The customer never pays a price the backend did not compute | Frontend prices are display-only; totals are always recomputed server-side |
| I3 | A seller can never read or mutate another seller's data | Seller isolation enforced in the service layer, not in the UI |
| I4 | A replayed or duplicated payment callback must be harmless | Webhook idempotency via unique event ids + signature verification |

Secondary goals: SEO for public pages, real-time responsiveness for dashboards, and testability without a live database.

## 2. Solution shape

```text
┌────────────────────────────────────────────────────────────────────┐
│                         Client Tier                                │
│  Next.js App Router (RSC + Client Components)  ·  Browser          │
└───────────────┬──────────────────────────────┬─────────────────────┘
                │ REST (JSON)                  │ WebSocket (SignalR)
                ▼                              ▼
┌────────────────────────────────────────────────────────────────────┐
│                        Marketplace.API                              │
│  Controllers · Middleware pipeline · AuthN/AuthZ · SignalR hubs    │
│  Swagger · Health checks · Serilog request logging · Rate limiting │
└───────────────┬────────────────────────────────────────────────────┘
                │ DTOs only (no entity leakage)
                ▼
┌────────────────────────────────────────────────────────────────────┐
│                     Marketplace.Application                        │
│  Use-case services · Commands/Queries · Validators · Mappings     │
│  Port interfaces: IRepository, ICacheService, IPaymentGateway,     │
│                   IEmailSender, IRealtimeNotifier, IBackgroundJob  │
│                   IUnitOfWork, ICurrentUser, IClock                 │
└───────────────┬────────────────────────────────────────────────────┘
                │ entity operations / business rules
                ▼
┌────────────────────────────────────────────────────────────────────┐
│                       Marketplace.Domain                           │
│  Entities · Enums · Value Objects · Domain Events                  │
│  Domain Exceptions · State machines · Calculation rules            │
│  ZERO framework dependencies                                       │
└───────────────┬────────────────────────────────────────────────────┘
                │ implemented by
                ▼
┌────────────────────────────────────────────────────────────────────┐
│                   Marketplace.Infrastructure                      │
│  EF Core (Npgsql/PostgreSQL) · Redis · Hangfire · Payment gateways │
│  Serilog sinks · File storage · Email · Seed data · Migrations     │
└────────────────────────────────────────────────────────────────────┘
```

### 2.1 Dependency rule

```text
API            ──depends-on──►  Application
Infrastructure ──depends-on──►  Application
Application   ──depends-on──►  Domain
Domain        ──depends-on──►  (nothing but the BCL)

API and Infrastructure do NOT depend on each other directly.
```

Enforcement is automated: `Marketplace.Domain.csproj` has **no** package references at all, and a unit test asserts that the Domain assembly's referenced assemblies are limited to the framework and `System.*`.

### 2.2 Why a modular monolith

Modules inside the single deployable are delimited by folders under `Application/Modules/*` (`Auth`, `Sellers`, `Catalog`, `Inventory`, `Cart`, `Checkout`, `Orders`, `Payments`, `Refunds`, `Commissions`, `Reviews`, `Coupons`, `Notifications`, `Audit`, `Analytics`, `Reports`). Each module owns its use cases, DTOs and validators and may only reach across module boundaries through another module's public service interface. This keeps the door open for extraction into services later without paying the cost of distributed transactions today.

## 3. Request lifecycle

```text
1. Serilog request-logging middleware          → correlation id
2. Exception-handling middleware               → ProblemDetails
3. Rate-limiting middleware (per IP / per user)
4. CORS middleware (allow-listed origins)
5. HTTPS redirection / HSTS
6. Authentication (JWT bearer)                 → ClaimsPrincipal
7. Authorization (policy based)                → role + resource ownership
8. Model binding + FluentValidation
9. Controller (thin: parse → call use case → map result)
10. Application service                        → business rules, transaction
11. Repository / DbContext                     → Npgsql
12. Redis cache (read-through, tagged keys)
13. Domain events dispatched                   → notifications, audit, SignalR
14. Result mapped to DTO                        → JSON
```

## 4. Data architecture

- **Provider** — PostgreSQL 14+ accessed through EF Core 8 with the Npgsql provider, Code First, Fluent API for all mapping, explicit indexes and check constraints.
- **Money** — `decimal(18,2)`. Never `float`/`double`.
- **Concurrency** — every mutable aggregate root carries a `byte[] RowVersion` mapped to PostgreSQL `xmax` (`IsRowVersion()`), so EF issues `UPDATE ... WHERE rowversion = @p` and throws `DbUpdateConcurrencyException` on a lost update. Inventory additionally uses conditional UPDATEs inside a serializable-ish transaction.
- **Soft delete** — `Product`, `Category`, `Seller`, `Coupon`, `Review` carry `IsDeleted`/`DeletedAt`; all queries filter them via a global query filter so deletes are auditable and reversible.
- **Time** — every timestamp is `DateTimeOffset` in UTC; `IClock` is injected so time-dependent rules are unit-testable.
- **IDs** — `Guid` v7-style (time-ordered) primary keys generated in the domain, which keeps PostgreSQL B-tree inserts append-friendly while remaining non-enumerable.

## 5. Caching strategy (Redis)

Caching is applied **only** to read-mostly, non-financial data.

| Cache | Key | TTL | Invalidated by |
| --- | --- | --- | --- |
| Product list pages | `catalog:products:{hash}` | 10 min | product create/update/delete, approval, price or stock change |
| Product detail | `catalog:product:{id}` / `catalog:product:slug:{slug}` | 15 min | same as above |
| Category tree | `catalog:categories` | 30 min | category CRUD |
| Store profile | `stores:profile:{slug}` | 15 min | seller/store update |
| Home page payload | `home:payload` | 5 min | featured product / banner change |

Never cached: stock availability during checkout, order totals, payment status, refund status, commissions, cart contents, notifications.

Tags (`catalog:product:*`) are tracked so a single mutation can evict a whole family of keys. When `Redis__Enabled=false` or the connection fails at startup, the container falls back to a process-local in-memory cache with identical semantics and logs a warning — development and CI never hard-depend on Redis.

## 6. Real-time architecture

One hub, `MarketplaceHub`, mapped at `/hubs/marketplace`.

| Group | Membership | Events delivered |
| --- | --- | --- |
| `user:{userId}` | every authenticated user | `NotificationCreated`, `OrderUpdated`, `PaymentUpdated`, `RefundUpdated`, `CartUpdated` |
| `order:{orderId}` | customer of that order + owning seller + admins | `OrderStatusChanged` |
| `seller:{sellerId}` | members of that seller | `SellerOrderCreated`, `SellerOrderUpdated`, `InventoryLow`, `NewReview` |
| `admin` | `Admin`, `SuperAdmin` | `SellerApplied`, `ProductPendingApproval`, `RefundRequested`, `PlatformMetricsUpdated` |

The browser never receives financial payloads it is not entitled to: notifications are projected to a minimal DTO before publishing.

## 7. Background processing

`BackgroundJobScheduler` (a thin abstraction) is implemented by Hangfire with an in-process `BackgroundService` fallback for environments where the persistent storage is not available.

| Job | Cadence | Responsibility |
| --- | --- | --- |
| `ReleaseExpiredReservationsJob` | every minute | release inventory reservations past `ExpiresAt` |
| `AbandonedCartCleanupJob` | hourly | clear carts untouched for 30 days, notify the customer once |
| `NotificationDispatchJob` | every 30 s | push pending notifications over SignalR + e-mail |
| `SellerPayoutJob` | daily 02:00 | convert released seller earnings into payout records |
| `TemporaryRecordCleanupJob` | daily 03:00 | purge expired refresh tokens, consumed webhooks and reset tokens |
| `AnalyticsSnapshotJob` | daily 04:00 | roll up daily seller/admin analytics series |
| `LowStockSweepJob` | every 15 min | detect threshold breaches and raise `InventoryLow` |

Every job is idempotent — re-running it must not double-apply an effect.

## 8. Frontend architecture

```text
Server Components (default)      Client Components (opt-in, "use client")
─────────────────────────────────  ──────────────────────────────────────────
homepage                         cart interactions
product detail (initial payload)  wishlist & quantity steppers
category listing                 filter / sort controls
store profile                    checkout wizard
sitemap / robots                 dashboards + Recharts
metadata generation              forms (React Hook Form + Zod)
```

State ownership is explicit:

- **Server state** → TanStack Query (products, cart, orders, notifications, reports).
- **Client/UI state** → Zustand (theme, sidebar, modals, auth UI session, checkout draft).
- **Form state** → React Hook Form + Zod.

SignalR events invalidate TanStack Query keys, they never write to Zustand — that keeps a single source of truth for server data and removes a whole class of staleness bugs.

## 9. Observability

- **Serilog** with console + rolling-file sinks; request/response logging with correlation ids; enrichment with `UserId` and `Role`.
- Redaction: `Password`, `Token`, `RefreshToken`, `Secret`, `Authorization` headers and card data are never written to logs.
- **Health checks** at `/health` (liveness, always healthy) and `/health/ready` (readiness: PostgreSQL, Redis, background worker heartbeat).
- **Audit logs** are a first-class table, not just log files, so admin actions are queryable.

## 10. Testing architecture

```text
UnitTests        → Domain + Application, no I/O. Pricing, commission,
                   coupon maths, inventory rules, order state machine,
                   validators. Fast, hundreds of tests.
IntegrationTests → WebApplicationFactory over the real API pipeline with a
                   relational SQLite database and a deterministic fake
                   clock/cache/gateway. Full HTTP, real auth, real
                   transactions, real authorization policies.
```

No database server and no Redis are required to run the suite, which keeps `dotnet test` viable on any machine and in CI.
