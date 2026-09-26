# Security Model

> Threat-oriented summary of how the marketplace defends money, stock and identity. Every control listed here is implemented in code and covered by tests.

## 1. Trust boundaries

```text
┌─ Untrusted ────────────────────────────────────────────────┐
│ Browser input, query strings, route params, request bodies,│
│ cookies, JWTs, webhook bodies, uploaded files              │
└────────────────────────────────────────────────────────────┘
                    │  validation · authentication · authorization
                    ▼
┌─ Server-trusted ───────────────────────────────────────────┐
│ EF Core model, domain entities, services, PostgreSQL,      │
│ Redis, payment gateways                                     │
└────────────────────────────────────────────────────────────┘
```

**Rule zero:** anything crossing the first boundary is untrusted. The frontend is a convenience layer, never a security control.

## 2. Authentication

### 2.1 Password storage

- PBKDF2-HMAC-SHA256, 100 000 iterations, 128-bit cryptographically random salt, 256-bit subkey.
- Constant-time comparison on verification.
- Passwords are never logged, never returned by any endpoint, and never stored in audit snapshots (the audit writer redacts `Password`, `PasswordHash`, `Token`, `RefreshToken`, `Secret` keys).

### 2.2 Token strategy

```text
Access token   JWT · 15 minutes · signed HS256 (Jwt__Key ≥ 32 chars)
               claims: sub, email, role, sid (seller), jti, iss, aud, iat, exp
Refresh token  opaque 64-byte random string (base64url)
               stored as SHA-256 hash only
               single-use, rotated on every refresh, 30-day expiry
               family id links every token descended from one login
```

**Refresh-token rotation with reuse detection**

```text
refresh(t1) ──▶ rotate ──▶ t2 (t1 revoked)
     │
     └── presenting t1 again ──▶ 401 + revoke t2..tN (family compromise assumed)
```

This turns a stolen token into a detectable event: the legitimate client's next refresh fails loudly instead of silently sharing a session with an attacker.

**Browser storage**

| Token | Where | Why |
| --- | --- | --- |
| Access token | memory (React context / module singleton) | short-lived, not readable after a tab closes |
| Refresh token | `HttpOnly`, `Secure`, `SameSite=Lax` cookie | never exposed to JS, not in `localStorage`/`sessionStorage`/Zustand persist |

`SameSite=Lax` + an explicit origin allow-list gives CSRF protection without a token cookie, because state-changing calls use `Authorization: Bearer` (not ambient cookies).

### 2.3 Account-level controls

- Login attempts: 10/min per IP **and** 5/min per email → `429`.
- Registration: 5/hour per IP.
- Failed logins are audit-logged with the email, IP and user agent — never the password.
- Email confirmation and password-reset tokens are single-use, 24-hour expiry, hashed at rest.

## 3. Authorization

### 3.1 Three-layer model

```text
Layer 1  Route policy        [Authorize(Policy = "SellerOnly")]   → coarse role gate
Layer 2  Ownership check     sellerId / customerId from the TOKEN → resource scope
Layer 3  State rules         status, approval, cancellation windows → business rules
```

Layer 2 is the one that matters most. The seller scope comes from the `sid` claim; a request body containing a different `sellerId` is **ignored**, not trusted. This is the single most important anti-IDOR control in the system.

### 3.2 Seller isolation

Every seller-scoped query is built through helpers that inject the predicate:

```csharp
// Application/Common/Extensions/QueryableExtensions.cs
public static IQueryable<Product> OwnedBySeller(this IQueryable<Product> query, Guid sellerId)
    => query.Where(p => p.SellerId == sellerId);
```

```csharp
// Seller product lookup — ownership is part of the query, not a post-filter
var product = await _products
    .Query()
    .OwnedBySeller(_currentUser.SellerId!.Value)
    .FirstOrDefaultAsync(p => p.Id == id, ct);
```

There is no code path that loads a product by id and then compares the seller id in memory — that pattern leaks existence and is a common source of cross-tenant bugs.

### 3.3 Ownership matrix

| Resource | Customer | Seller | Admin |
| --- | --- | --- | --- |
| Own order | read / cancel | — | read / status |
| Own cart, wishlist, address | read / write | — | — |
| Own review | create / edit / delete | reply | moderate |
| Any order | — | own sub-orders only | read / status |
| Any product | read (published) | own only | read / approve / feature |
| Coupon | validate at checkout | own only | all |
| Inventory | — | own only | read |
| Refund | request own | respond on own sub-order | review / decide |
| Audit logs | — | — | read |
| Users & roles | own profile | own profile | all |

## 4. Input validation

Three independent layers, deliberately:

```text
Request DTO  →  FluentValidation on the server (authoritative)
Form input   →  Zod in the browser (fast feedback, shape parity)
ORM queries  →  parameterized LINQ only (no string-concatenated SQL)
```

Additional rules:

- Every `*Request` DTO has a matching validator; `AddValidatorsFromAssembly` makes forgetting one impossible to ship silently (a startup self-check logs a warning listing DTOs without validators).
- `pageSize` is hard-capped at 100; `search` is trimmed and truncated to 200 characters; sort keys are resolved through an enum whitelist so they can never reach the query as free text.
- File uploads accept an extension + content-type + size allow-list and are stored under generated names — the client filename is never used on disk.
- `HttpContext` values used in audit logs (`IpAddress`, `UserAgent`) are truncated to prevent log-forging via header injection.

## 5. Payments & webhooks

| Threat | Control |
| --- | --- |
| Forged webhook | HMAC-SHA256 over the raw body, compared in constant time; invalid → `409`, logged, no state change |
| Replayed webhook | unique index on `(provider, provider_event_id)`; duplicate → `200` no-op |
| Amount tampering | webhook amount and order id are compared to the stored payment; mismatch rejected |
| Client-side fake success | order/payment state only ever changes from a verified server-side event — the browser callback is cosmetic |
| Double capture | `Payment.Status` transitions use a conditional update (`WHERE status = 'Initiated'`); a second capture affects 0 rows |
| Amount/currency confusion | order currency is stored on the payment and compared to the gateway amount |

`Idempotency-Key` is additionally accepted on `POST /api/orders` and `POST /api/payments` so a client retry cannot create a duplicate order.

## 6. Inventory integrity

- Checkout cannot oversell: a single conditional `UPDATE` acts as the lock (see [`ARCHITECTURE.md`](../architecture/ARCHITECTURE.md) §4).
- Every quantity change is an immutable `InventoryTransaction` row, so a dispute can always be reconstructed.
- Reservations are time-boxed and released by a background job; release is idempotent (`WHERE reserved_quantity >= @qty`).
- Manual seller adjustments require a reason and are audit-logged with before/after values.

## 7. Transport & headers

```text
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: geolocation=(), microphone=(), camera=()
Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline' <nonce>;
                         img-src 'self' data: https:; frame-ancestors 'none'
```

- CORS: explicit origin allow-list from `Cors:AllowedOrigins`; `AllowCredentials` is only enabled when the origin list is non-empty, so a misconfiguration cannot produce `*` + credentials.
- HTTPS enforced in production via `UseHttpsRedirection` + HSTS.

## 8. Secrets management

```text
ConnectionStrings__DefaultConnection
Jwt__Key, Jwt__Issuer, Jwt__Audience
Redis__ConnectionString, Redis__Enabled
Payment__ApiKey, Payment__Secret, Payment__WebhookSecret
Smtp__Host, Smtp__Username, Smtp__Password
```

- Values come from environment variables or user-secrets in development; `appsettings.json` contains **no** production secrets, only non-sensitive defaults.
- `.env` is git-ignored; `.env.example` documents every variable with a placeholder.
- Nothing sensitive is prefixed `NEXT_PUBLIC_` — the browser bundle contains no server secret.
- A startup validation step refuses to boot in `Production` when `Jwt__Key` is missing, shorter than 32 characters, or equal to the development default.

## 9. Logging & privacy

Redacted keys in every log sink and every audit snapshot:

```text
password, passwordhash, passwordconfirm, token, refreshtoken,
secret, apikey, authorization, cookie, cardnumber, cvv
```

Logs contain identifiers, not personal data dumps: order numbers, entity ids, correlation ids, IP addresses. Customer names and emails are only logged at `Debug` level, and never for anonymous storefront traffic.

## 10. Dependency & supply chain

- `dotnet list package --vulnerable --include-transitive` and `npm audit` run in CI.
- Versions are pinned with central package management (`Directory.Packages.props`) and automated Dependabot PRs.
- No package is added without a stated purpose; unused UI frameworks are explicitly banned.

## 11. OWASP Top 10 coverage

| Risk | Mitigation |
| --- | --- |
| A01 Broken access control | Policy + ownership + state layers; seller isolation query helpers; integration tests for cross-tenant access |
| A02 Cryptographic failures | PBKDF2 password hashing; SHA-256 refresh tokens; HTTPS + HSTS; no secrets in the repo |
| A03 Injection | Parameterized LINQ only; FluentValidation + Zod; no raw SQL outside the migration folder |
| A04 Insecure design | Money and stock rules live in the Domain/Application layers, not in controllers or React |
| A05 Security misconfiguration | No wildcard CORS, no verbose errors in production, no dev secrets in `appsettings.json` |
| A06 Vulnerable components | Vulnerability scanning in CI, pinned versions |
| A07 Identification & auth failures | Rotation + reuse detection, rate limits, generic login error messages |
| A08 Software/data integrity | Signed webhooks, idempotency keys, lockfile-based installs, `npm ci` in CI |
| A09 Logging & monitoring failures | Serilog with correlation ids, health checks, audit-log table, structured alerting hooks |
| A10 SSRF | No user-supplied outbound URLs; the webhook relay only forwards to configured gateway endpoints |

## 12. Security verification in CI

```text
dotnet build                 compilation
dotnet test                  unit + integration, including the authorization matrix
npm audit --audit-level=high dependency advisories
dotnet list package --vulnerable --include-transitive
secret scanning               no .env / no JWT-looking literals in the tree
```

The integration suite includes a dedicated `AuthorizationIsolationTests` fixture that logs in as seller A, seller B and a customer and asserts `403` on every cross-boundary endpoint listed in §3.3.
