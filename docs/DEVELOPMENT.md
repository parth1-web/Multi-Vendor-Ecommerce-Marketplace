# Development Workflow

> The operating manual for this repository: environment, commands, conventions, commit style, and the manual verification checklists used in place of a frontend test runner.

## 1. Prerequisites

```text
.NET SDK 8.0.x        dotnet --version
Node.js 20 LTS + npm 10
PostgreSQL 14+        optional for tests, required to run the API
Redis 6+              optional — in-memory cache is used when unavailable
Git 2.40+
```

## 2. Clone and build

```bash
git clone https://github.com/parth1-web/Multi-Vendor-Ecommerce-Marketplace.git
cd Multi-Vendor-Ecommerce-Marketplace

# backend
cd backend
dotnet restore
dotnet build

# frontend
cd ../frontend/marketplace-web
npm install
npm run build
```

## 3. Local configuration

### Backend

Migrations and the demo seed only run outside Production, so a local run needs the
Development environment. Without it the API starts against an empty schema and every
catalogue request fails on a missing table.

```powershell
# PowerShell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Database=marketplace;Username=postgres;Password=<password>"
$env:Jwt__Key = "<at-least-32-characters-development-key>"
$env:Redis__Enabled = "false"
$env:Payment__DefaultProvider = "Mock"
```

The database itself is not created for you: create it once with
`createdb -U postgres marketplace`, or let `dotnet ef database update` make it. The
connection string in `appsettings.json` names it in lower case, which is what a default
PostgreSQL install uses.

The checked-in `Jwt:Key` is a development key and the API refuses it when the environment
is Production, so an unset secret cannot reach a deployed environment by accident.

Development secrets can also live in user-secrets:

```bash
dotnet user-secrets init --project src/Marketplace.API
dotnet user-secrets set "Jwt:Key" "..." --project src/Marketplace.API
```

### Frontend

```bash
cd frontend/marketplace-web
npm install
cp .env.example .env.local
npm run dev
```

`NEXT_PUBLIC_API_URL` must be reachable from the browser as well as from the Next server,
and the API's `Cors:AllowedOrigins` must list the address the frontend is served from. The
default pair is `http://localhost:5000` and `http://localhost:3000`.

```dotenv
NEXT_PUBLIC_API_URL=http://localhost:5000
NEXT_PUBLIC_SIGNALR_URL=http://localhost:5000/hubs/marketplace
NEXT_PUBLIC_SITE_URL=http://localhost:3000
NEXT_PUBLIC_PAYMENT_RETURN_URL=http://localhost:3000/checkout
```

## 4. Database migrations

```bash
cd backend

# add a migration
dotnet ef migrations add AddProductApprovals \
  --project src/Marketplace.Infrastructure \
  --startup-project src/Marketplace.API \
  --output-dir Persistence/Migrations

# apply
dotnet ef database update --project src/Marketplace.Infrastructure --startup-project src/Marketplace.API

# inspect
dotnet ef migrations list --project src/Marketplace.Infrastructure --startup-project src/Marketplace.API

# reset
dotnet ef database drop --force --project src/Marketplace.Infrastructure --startup-project src/Marketplace.API
```

Migrations live in `Marketplace.Infrastructure/Persistence/Migrations` and are the only place raw SQL is allowed.

## 5. Run

```bash
# API      → https://localhost:7xxx  · /swagger · /health
dotnet run --project src/Marketplace.API

# frontend → http://localhost:3000
cd frontend/marketplace-web && npm run dev
```

## 6. Commands

| Purpose | Command |
| --- | --- |
| Build backend | `dotnet build` |
| Run backend | `dotnet run --project src/Marketplace.API` |
| Run all tests | `dotnet test` |
| Unit tests only | `dotnet test tests/Marketplace.UnitTests` |
| Integration only | `dotnet test tests/Marketplace.IntegrationTests` |
| Test with coverage | `dotnet test --collect:"XPlat Code Coverage"` |
| Lint frontend | `npm run lint` |
| Typecheck frontend | `npm run typecheck` |
| Build frontend | `npm run build` |
| Start frontend (prod) | `npm start` |
| Clean artifacts | `dotnet clean` / `npm run clean` |

## 7. Coding conventions

### C#

- `file`-scoped namespaces, one type per file, filename matches the type.
- `PascalCase` types and members, `_camelCase` private fields, `camelCase` locals and parameters.
- `var` only when the type is obvious from the right-hand side.
- Async all the way down; `CancellationToken` flows from controller to repository.
- No `public` setters on domain entities; state changes go through intent-revealing methods.
- `switch` expressions over long `if/else` chains; pattern matching for type checks.
- XML doc comments on public domain and application APIs; not required for trivial members.

### TypeScript / React

- `strict: true`, no `any`; `unknown` at API boundaries and narrowed before use.
- `interface` for object shapes, `type` for unions.
- Components: `PascalCase.tsx`. Hooks: `useThing.ts`.
- `function` declarations for components, arrow functions for inline handlers and callbacks.
- Server components by default; `"use client"` only with a stated reason.
- No barrel re-exports of large libraries (bundle cost).

### Folder discipline

```text
Business rule                → Domain, then Application. Never API, never React.
Persistence                  → Infrastructure only.
HTTP concerns                → API only.
Presentation state           → components/store only.
```

## 8. Commit convention

[Conventional Commits](https://www.conventionalcommits.org/), enforced by the `repository.yml` workflow.

```text
<type>(<optional scope>): <imperative summary under 72 chars>
```

| Type | Use for |
| --- | --- |
| `feat` | new capability |
| `fix` | bug fix |
| `refactor` | behaviour-preserving change |
| `perf` | performance improvement |
| `test` | tests only |
| `docs` | documentation |
| `build` | build system, package versions |
| `ci` | workflows, pipelines |
| `chore` | tooling, housekeeping |
| `style` | formatting only |

Scopes used in this repository: `auth`, `sellers`, `categories`, `products`, `inventory`, `cart`, `checkout`, `orders`, `payments`, `refunds`, `commissions`, `reviews`, `coupons`, `notifications`, `realtime`, `cache`, `jobs`, `audit`, `analytics`, `frontend`, `db`.

```bash
git add <paths>            # stage intentionally, never `git add -A` blindly
git commit -m "feat(inventory): add reservation expiry release job"
git push origin main
```

## 9. Manual verification checklist

Run through this list whenever a customer-facing route changes. It replaces a frontend unit-test runner and is the acceptance gate for UI work.

### Navigation

- [ ] Header links, mega-menu, footer links, breadcrumb trail all resolve.
- [ ] Sidebar routes highlight the active item on every dashboard route.
- [ ] Browser back/forward preserves scroll and query state on list pages.
- [ ] 404 renders for an unknown product, category and store slug.

### Authentication

- [ ] Register validation messages match server rules.
- [ ] Login redirects to the `returnUrl` when present.
- [ ] Refresh-token expiry surfaces a session-expired toast and routes to login.
- [ ] Role mismatch (customer opening `/admin`) is redirected, and the API still returns `403`.
- [ ] Logout clears the session everywhere, including the notification bell count.

### Forms

- [ ] Labels are associated; errors are announced and linked with `aria-describedby`.
- [ ] Submit is disabled while in flight and shows a spinner without layout shift.
- [ ] Server `400` field errors are mapped onto the correct inputs.
- [ ] Zod and FluentValidation rules agree (spot-check every shared rule).

### States

- [ ] Skeletons match the final layout (no content jump).
- [ ] Empty states offer a next action.
- [ ] Error states offer retry.
- [ ] Long lists paginate and preserve filters.

### Responsive

- [ ] Verified at 1920×1080, 1440×900, 1366×768, 1024×768, 768×1024, 430×932, 390×844, 360×800.
- [ ] No horizontal scroll at 360 px.
- [ ] Off-canvas nav and filters trap and restore focus.

### Accessibility

- [ ] Full keyboard traversal of the primary flow.
- [ ] Visible focus ring everywhere.
- [ ] Status is never colour-only.
- [ ] Light and dark themes both meet contrast thresholds.
- [ ] `prefers-reduced-motion` removes animation.

### Real-time

- [ ] Kill the network: the reconnecting indicator appears; on reconnect data refreshes.
- [ ] A new order raised by another session appears without a manual refresh.
- [ ] The notification badge increments live.

## 10. Definition of done

See [`testing/TESTING.md`](testing/TESTING.md) §9 — the same checklist applies to backend work, plus `dotnet test` green and documentation updated.
