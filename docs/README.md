# Development

The operating manual: environment, commands, conventions, commit style, and the manual
verification checklists used in place of a frontend test runner.

## Run the whole system

```text
PostgreSQL 14+ on localhost:5432, role postgres/postgres    the one prerequisite
```

Nothing else has to be configured. The API creates the `marketplace` database, applies the
migrations and seeds the demo catalogue on its first start.

**In VS Code** — `Ctrl+Shift+B`, or F5, or *Terminal > Run Task… > Full stack (API + site)*.
The API and the site open side by side; the site is at <http://localhost:3000> and the API's
Swagger at <http://localhost:5000/swagger>. Stopping the task stops both.

**In a terminal**

```powershell
pwsh scripts/dev.ps1
```

Both processes run in the foreground, interleaved, and `Ctrl+C` stops both. It writes
`frontend/marketplace-web/.env.local` for the ports it uses, so the two halves cannot end up
pointing at different APIs.

Other tasks in `.vscode/tasks.json`: *API only*, *Site only*, *Test the API*, *Check the site*,
*Check everything*, *Reset the demo data*, *Install the site*, *Restore packages*.

### The ports, and why they are not configurable per machine

| | |
|---|---|
| API | `http://localhost:5000` |
| Site | `http://localhost:3000` |

Both are the defaults baked into the code, so a fresh clone works with no `.env` file at all. A
local `.env.local` that disagrees with them is the one way to end up with a site talking to an
API that is not running; `scripts/dev.ps1` writes it for you.

## Signing in

The demo seed creates these accounts, so every part of the system can be walked through without
registering anything:

| Role | Email | Password |
|---|---|---|
| Super admin | `admin@marketplace.dev` | `Super@123` |
| Manager | `manager@marketplace.dev` | `Admin@123` |
| Seller (TechWorld) | `seller@marketplace.dev` | `Seller@123` |
| Seller (FashionHub) | `fashion@marketplace.dev` | `Seller@123` |
| Customer | `customer@marketplace.dev` | `Customer@123` |

The seller and admin areas are at `/seller` and `/admin`; both redirect to sign-in if you arrive
without the right role. The payment provider is a sandbox, so paying an order is a button press
and nothing is charged.

## Prerequisites

```text
.NET SDK 8.0.x        dotnet --version
Node.js 20 LTS + npm 10
PostgreSQL 14+        required to run the API and the integration tests
Redis 6+              optional — in-memory cache is used when unavailable
Git 2.40+
```

## Clone and build

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
