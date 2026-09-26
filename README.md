<div align="center">

# Multi-Vendor E-Commerce Marketplace

**Production-grade marketplace platform** — ASP.NET Core 8 Clean Architecture backend, PostgreSQL, Redis, SignalR and a Next.js App Router storefront.

[![Backend Build](https://github.com/parth1-web/Multi-Vendor-Ecommerce-Marketplace/actions/workflows/backend.yml/badge.svg)](https://github.com/parth1-web/Multi-Vendor-Ecommerce-Marketplace/actions/workflows/backend.yml)
[![Frontend Build](https://github.com/parth1-web/Multi-Vendor-Ecommerce-Marketplace/actions/workflows/frontend.yml/badge.svg)](https://github.com/parth1-web/Multi-Vendor-Ecommerce-Marketplace/actions/workflows/frontend.yml)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Next.js 15](https://img.shields.io/badge/Next.js-15-000000?logo=nextdotjs&logoColor=white)](https://nextjs.org/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![License](https://img.shields.io/badge/license-MIT-10b981?logo=opensourceinitiative&logoColor=white)](LICENSE)

</div>

---

## Table of Contents

- [Overview](#overview)
- [Feature Map](#feature-map)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Repository Layout](#repository-layout)
- [Quick Start](#quick-start)
- [Environment Variables](#environment-variables)
- [Default Accounts](#default-accounts)
- [Testing](#testing)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [License](#license)

---

## Overview

A **multi-vendor marketplace** in the mould of Amazon or Daraz, where many independent sellers list products on one storefront, every checkout is automatically **split into per-seller sub-orders**, inventory is reserved transactionally, payments are verified through webhooks, and the marketplace takes a commission on every sale.

This is a portfolio-grade reference implementation: not a tutorial CRUD app, but a system that demonstrates domain modelling, concurrency control, idempotency, real-time delivery, caching, background processing and auditability.

## Feature Map

| Domain | Capabilities |
| --- | --- |
| **Authentication** | Register, login, JWT access tokens, refresh-token rotation with reuse detection, logout, `/auth/me`, email-confirmation & reset-password tokens |
| **Authorization** | RBAC (`SuperAdmin`, `Admin`, `Seller`, `Customer`), policy-based endpoint guards, resource ownership, mandatory **seller isolation** |
| **Sellers** | Seller application, admin approval / rejection / suspension, store profile, payout ledger, store SEO page |
| **Categories** | Hierarchical tree, slugs, activation, breadcrumb trail, admin CRUD |
| **Products** | CRUD, image gallery, variants, specifications, tags, search, filtering, sorting, server-side pagination, admin approval workflow |
| **Inventory** | Available / reserved / sold quantities, reservations with expiry, inventory transactions, low-stock detection, optimistic-concurrency protection |
| **Cart** | Add, update, remove, clear, save-for-later, automatic **seller grouping** |
| **Checkout** | Price, stock, coupon & address validation → totals → inventory reservation → order creation → seller-order split → payment |
| **Orders** | Marketplace order + per-seller sub-orders, legal state machine, cancellation, order tracking timeline |
| **Payments** | Abstracted `IPaymentGateway` (Mock, CashOnDelivery, Khalti, eSewa, Stripe), verification, signed webhooks with idempotency & replay protection |
| **Refunds** | Customer request → admin review → approve / reject → gateway refund → order + notification update |
| **Commissions** | Rate-based marketplace cut, historical rate snapshot per seller order, seller earnings, payouts |
| **Reviews** | Verified-purchase rule, one review per order item, ratings aggregation, moderation, seller replies |
| **Coupons** | Percentage / fixed, min-order, max-discount, date window, global & per-user limits, seller and product restrictions |
| **Notifications** | 17 typed events, unread counts, read / read-all, real-time push |
| **Real-Time** | SignalR `MarketplaceHub` with `user:{id}`, `seller:{id}`, `order:{id}`, `admin` groups |
| **Caching** | Redis product / category / store caches, tagged invalidation, graceful in-memory fallback |
| **Background Jobs** | Reservation release, notification dispatch, abandoned-cart cleanup, payout processing, temp-record cleanup |
| **Audit Logs** | Actor, action, entity, before/after diff, IP, correlation id, admin UI with filtering |
| **Analytics** | Admin, seller and customer metric aggregates, series data for charts |
| **Storefront** | SEO-first Next.js App Router: home, listing, product, category, store, search |
| **Dashboards** | Customer, seller and admin areas with Recharts analytics and dark mode |
| **Operations** | Serilog, health checks (`/health`), OpenAPI/Swagger, GitHub Actions CI |

## Tech Stack

**Backend** — C# · .NET 8 · ASP.NET Core Web API · EF Core 8 · PostgreSQL · StackExchange.Redis · SignalR · JWT Bearer · FluentValidation · Serilog · Swagger · Hangfire · xUnit · Moq

**Frontend** — Next.js 15 (App Router) · TypeScript · Bootstrap 5 · React-Bootstrap · TanStack Query · Zustand · Axios · React Hook Form · Zod · Recharts · Lucide React · `@microsoft/signalr`

**DevOps** — Git · GitHub · GitHub Actions · environment-based configuration

Deliberately **not** used: Docker, Docker Compose, Tailwind CSS, Material UI, Chakra UI, Ant Design.

## Architecture

```text
                       ┌──────────────────────────────┐
                       │        User Browser          │
                       └───────────────┬──────────────┘
                                       │
                                       ▼
                       ┌──────────────────────────────┐
                       │           Next.js             │
                       │  App Router · Server/Client  │
                       │  Metadata · SEO · Images     │
                       └───────────────┬──────────────┘
                                       │
                             REST / SignalR
                                       │
                                       ▼
                       ┌──────────────────────────────┐
                       │      ASP.NET Core Web API    │
                       │  Controllers · Auth · Hubs   │
                       └───────────────┬──────────────┘
                                       │
                                       ▼
                       ┌──────────────────────────────┐
                       │         Application          │
                       │  Services · DTOs · Validators│
                       └───────────────┬──────────────┘
                                       │
                                       ▼
                       ┌──────────────────────────────┐
                       │            Domain            │
                       │ Entities · VOs · Events      │
                       └───────────────┬──────────────┘
                                       │
                                       ▼
                       ┌──────────────────────────────┐
                       │        Infrastructure       │
                       │  EF Core · PostgreSQL · Redis│
                       │  Payments · Jobs · Email     │
                       └──────────────────────────────┘
```

A **modular monolith**: one deployable API, four layers with a strict dependency rule.

```text
API ─────────────► Application ───► Domain ◄─── Infrastructure
                                ▲
                                └── (abstractions live in Application)
```

- **Domain** has zero framework dependencies — no EF Core, no ASP.NET Core, no Redis.
- **Application** owns use cases, DTOs, validators and *interfaces*.
- **Infrastructure** implements those interfaces (repositories, Redis, gateways, jobs).
- **API** is a thin HTTP/SignalR shell: controllers, middleware, DI, Swagger, health checks.

## Repository Layout

```text
Multi-Vendor-Ecommerce-Marketplace/
├── backend/
│   ├── Marketplace.sln
│   ├── Directory.Build.props
│   ├── src/
│   │   ├── Marketplace.Domain/
│   │   ├── Marketplace.Application/
│   │   ├── Marketplace.Infrastructure/
│   │   └── Marketplace.API/
│   └── tests/
│       ├── Marketplace.UnitTests/
│       └── Marketplace.IntegrationTests/
├── frontend/
│   └── marketplace-web/
├── docs/
│   ├── architecture/
│   ├── api/
│   ├── database/
│   ├── deployment/
│   ├── frontend/
│   └── testing/
├── .github/workflows/
└── README.md
```

## Quick Start

### Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) and npm 10+
- [PostgreSQL 14+](https://www.postgresql.org/download/) (16+ recommended)
- [Redis](https://redis.io/) *(optional — an in-memory cache is used automatically when Redis is unreachable)*

### 1 · Backend

```bash
cd backend
dotnet restore
dotnet build

# point at your database, then create the schema
set ConnectionStrings__DefaultConnection=Host=localhost;Database=Marketplace;Username=postgres;Password=YOUR_PASSWORD
dotnet ef database update --project src/Marketplace.Infrastructure --startup-project src/Marketplace.API

dotnet run --project src/Marketplace.API
```

API: `https://localhost:7xxx` · Swagger: `/swagger` · Health: `/health`

### 2 · Frontend

```bash
cd frontend/marketplace-web
npm install

set NEXT_PUBLIC_API_URL=http://localhost:5000
set NEXT_PUBLIC_SIGNALR_URL=http://localhost:5000/hubs/marketplace

npm run dev
```

Storefront: `http://localhost:3000`

## Environment Variables

### Frontend (public, browser-visible)

| Variable | Purpose |
| --- | --- |
| `NEXT_PUBLIC_API_URL` | Base URL of the ASP.NET Core API |
| `NEXT_PUBLIC_SIGNALR_URL` | Base URL of the SignalR hub |
| `NEXT_PUBLIC_SITE_URL` | Canonical site origin, used for canonical URLs, Open Graph and the sitemap |
| `NEXT_PUBLIC_SITE_NAME` | Brand name used in metadata and the UI |

### Backend (server-only, never exposed to the browser)

| Variable | Purpose |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt__Key` | Signing key — must be ≥ 32 characters, rotate for production |
| `Jwt__Issuer` / `Jwt__Audience` | Token issuer and audience |
| `Jwt__AccessTokenMinutes` / `Jwt__RefreshTokenDays` | Token lifetimes |
| `Redis__ConnectionString` | Redis connection string |
| `Redis__Enabled` | Toggle Redis (`false` falls back to in-memory caching) |
| `Payment__DefaultProvider` | `Mock`, `CashOnDelivery`, `Khalti`, `ESewa`, `Stripe` |
| `Payment__WebhookSecret` | HMAC secret used to verify webhook signatures |
| `Serilog__MinimumLevel` | Serilog minimum level |
| `Marketplace__CommissionRate` | Default marketplace commission percentage |

## Default Accounts

Seeded on first run (development only):

| Role | Email | Password |
| --- | --- | --- |
| SuperAdmin | `admin@marketplace.dev` | `Admin@123` |
| Admin | `manager@marketplace.dev` | `Admin@123` |
| Seller | `seller@marketplace.dev` | `Seller@123` |
| Seller | `fashion@marketplace.dev` | `Seller@123` |
| Customer | `customer@marketplace.dev` | `Customer@123` |

> Change or remove these accounts before any non-local deployment.

## Testing

```bash
# backend
cd backend
dotnet test

# frontend
cd frontend/marketplace-web
npm run lint
npm run build
```

Backend tests are split into `Marketplace.UnitTests` (business rules: pricing, commission, coupons, inventory, order state machine) and `Marketplace.IntegrationTests` (full HTTP pipeline through `WebApplicationFactory` against a relational SQLite database, covering authentication, authorization, seller isolation, checkout, payments, webhooks and refunds).

## Documentation

| Document | Location |
| --- | --- |
| Architecture overview | [`docs/architecture/ARCHITECTURE.md`](docs/architecture/ARCHITECTURE.md) |
| Database design & ER model | [`docs/database/DATABASE_DESIGN.md`](docs/database/DATABASE_DESIGN.md) |
| API documentation | [`docs/api/API_DOCUMENTATION.md`](docs/api/API_DOCUMENTATION.md) |
| Security model | [`docs/SECURITY.md`](docs/SECURITY.md) |
| Frontend architecture | [`docs/frontend/FRONTEND_ARCHITECTURE.md`](docs/frontend/FRONTEND_ARCHITECTURE.md) |
| Testing strategy | [`docs/testing/TESTING.md`](docs/testing/TESTING.md) |
| Deployment guide | [`docs/deployment/DEPLOYMENT.md`](docs/deployment/DEPLOYMENT.md) |
| Development workflow | [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) |

## Contributing

1. Create a topic branch: `git checkout -b feat/your-feature`
2. Follow the existing layering — business rules belong in Domain/Application, never in controllers
3. Keep `Domain` framework-free
4. Add or update tests for every business rule you touch
5. Verify `dotnet build`, `dotnet test` and `npm run build`
6. Use conventional commits: `feat(scope):`, `fix(scope):`, `refactor:`, `test:`, `docs:`, `ci:`, `chore:`

## License

Released under the [MIT License](LICENSE).

---

<div align="center">
  Built with Next.js, TypeScript, Bootstrap 5, ASP.NET Core 8, EF Core, PostgreSQL, Redis and SignalR.
</div>
