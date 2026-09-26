# Testing Strategy

> Two backend test projects and a frontend validation pipeline. The suite is designed to run **without PostgreSQL or Redis** so `dotnet test` works on any machine and in CI.

## 1. Test pyramid

```text
                 ╱╲
                ╱  ╲          Integration tests (~70)
               ╱ API╲         full HTTP pipeline, real auth, real transactions
              ╱──────╲        relational SQLite, fakes for clock/cache/gateway
             ╱        ╲
            ╱  Unit    ╲      Unit tests (~180)
           ╱  (Domain   ╲     no I/O — pure business rules
          ╱  + App)     ╲
         ╱──────────────╲
        ╱   Domain model  ╲    entities, state machines, calculations
       ╱____________________╲
```

| Suite | Project | Scope | Dependencies |
| --- | --- | --- | --- |
| Unit | `backend/tests/Marketplace.UnitTests` | `Domain`, `Application` | none |
| Integration | `backend/tests/Marketplace.IntegrationTests` | whole API | in-memory relational DB, fake clock, in-memory cache, mock gateway |
| Frontend | `npm run lint`, `npm run build`, `tsc --noEmit` | types + bundling | none |

## 2. Why SQLite for integration tests

Production runs PostgreSQL. The integration suite runs on **SQLite in-memory** because:

1. Zero setup — no service, no credentials, no ports. `git clone && dotnet test` just works.
2. Fast enough to run the full HTTP stack on every save.
3. Still a *relational* database, so foreign keys, unique constraints, check constraints, `DELETE`/`UPDATE` semantics and transactions behave realistically — which is what the concurrency and idempotency tests actually depend on.

Provider-specific behaviour (PostgreSQL `xmin` row versions, `jsonb`, `tsvector` full-text) is guarded:

- The concurrency token is configured by a provider switch — `xmax` on Npgsql, an integer counter on SQLite — behind `DatabaseOptions.UsePostgresConcurrencyToken`.
- Full-text search falls back to a `LIKE` implementation registered by `ISearchQueryTranslator` when the Npgsql translator is unavailable.
- The test fixture asserts which provider it is running so a failure is never silently misdiagnosed.

## 3. Unit-test coverage by rule

| Area | Examples |
| --- | --- |
| **Pricing** | `OrderPricingCalculatorTests` — subtotal, per-seller shipping allocation, coupon application, cap on maximum discount, tax, rounding to 2 dp, negative-total guard |
| **Commission** | `CommissionCalculatorTests` — 10% of 10 000 = 1 000 / seller gets 9 000; per-seller-order basis; rate snapshot immutability; zero-rate sellers |
| **Coupons** | `CouponValidatorTests` — percentage vs fixed, minimum order, maximum discount cap, date window, expired, exhausted global limit, per-user limit, seller/product scoping, ineligible subtotal |
| **Inventory** | `InventoryTests` — reserve within available, reject over-reserve, release restores exactly once, double release is a no-op, adjust with reason, low-stock threshold, reserved+sold invariants |
| **Order state machine** | `OrderStatusTransitionTests` — legal path, every illegal transition throws, terminal states are final, cancel window, `Pending → Shipped` rejected |
| **Multi-seller split** | `OrderSplitTests` — one order per customer, N sub-orders for N sellers, per-seller totals sum to the marketplace total, coupon allocated to eligible sellers only |
| **Validators** | Every `FluentValidation` validator: required fields, ranges, formats, cross-field rules |
| **Review eligibility** | `ReviewEligibilityTests` — not the buyer → reject, not delivered → reject, already reviewed → reject, eligible → accept |
| **Slug & text** | `SlugGeneratorTests` — unicode folding, punctuation, uniqueness suffixing, length cap |
| **Domain model** | entity invariants, value-object equality, domain events raised |

## 4. Integration-test coverage

### 4.1 Authentication

```text
register → 201 + user created + welcome notification
register with duplicate email → 409 with field error
register with weak password → 400
login with wrong password → 401 with a generic message
login with unknown email → 401 (same message, no user enumeration)
login → access token + refresh token
refresh rotates: old token invalid, new token valid
refresh with a revoked token → 401 AND the whole family is revoked
logout → refresh token no longer works
GET /auth/me → correct identity, role and seller id
```

### 4.2 Authorization & isolation

```text
customer → /api/admin/users            → 403
customer → /api/sellers/{id}/store     → 403
seller A → PUT /api/products/{B's id}  → 403
seller A → GET /api/seller-orders/{B}  → 403
seller A → GET /api/inventory           → only A's rows
anonymous → /api/orders                 → 401
suspended seller → seller endpoints     → 403
admin → seller status change           → 200 + audit row + notification
customer A → GET /api/orders/{B's id}   → 403
```

### 4.3 Inventory

```text
checkout exceeding stock                     → 409 insufficient_stock
reservation created and visible in ledger
reservation released on order cancel
concurrent reservations for the last unit     → exactly one succeeds
double release of the same reservation        → no-op, balance intact
seller stock adjustment requires a reason
low-stock threshold breach → InventoryLow notification to the seller
```

### 4.4 Checkout & orders

```text
quote matches the final order totals
multi-seller cart → 1 order + N seller orders
coupon applied → discount recorded per seller order
order items snapshot price/name/image
cart cleared after successful checkout
checkout with an out-of-stock item → 409, no order created
order status transitions validated
customer cancels a Pending order → reservation released
illegal transition (Pending → Delivered) → 409
order timeline exposes timestamps
```

### 4.5 Payments & webhooks

```text
payment created for an order
verify endpoint settles a Mock payment
valid webhook → order marked paid, commissions created, notifications emitted
duplicate webhook event id → 200, no second effect
invalid signature → 409, state unchanged
amount mismatch → 409, state unchanged
webhook for an unknown payment → 404
CashOnDelivery skips the gateway and settles on delivery
Idempotency-Key replay returns the original order
```

### 4.6 Refunds, reviews, coupons, notifications

```text
refund requested on a delivered item → 201
duplicate refund request for the same item → 409
admin approves → gateway called, stock returned, commission reversed
admin rejects with a reason → customer notified
review by a verified buyer → 201, product rating recomputed
review by a non-buyer → 403
second review on the same item → 409
seller reply → 201, visible to the review author
coupon validation explains the rejection reason
coupon per-user limit exhausted → 422
notification unread count, read, read-all
```

## 5. Frontend validation

No unit-test runner is introduced (keeping the dependency surface small, as the project brief requires). Quality is enforced by:

```text
npm run lint        eslint --max-warnings=0
npm run typecheck   tsc --noEmit
npm run build       next build (production compiler, route prerendering, bundle analysis)
```

Plus manual verification checklists in [`../DEVELOPMENT.md`](../DEVELOPMENT.md) covering navigation, auth, forms, loading/error/empty states, keyboard access, the eight target viewports and real-time updates.

## 6. Test data strategy

`MarketplaceTestDataBuilder` produces a deterministic world:

```text
1 super admin, 1 admin, 2 approved sellers, 1 pending seller, 1 suspended seller
3 customers
6 categories (2 levels)
24 products across 4 categories with variants, images and stock
coupons: global percentage, global fixed-capped, seller-scoped, expired, exhausted
reviews on delivered items
```

- Deterministic: fixed GUIDs, fixed `DateTimeOffset` values, a frozen `IClock`.
- No dependency between tests — each test composes only what it needs through the builder.
- Seed data in production code is separate and idempotent, guarded to development only.

## 7. Running

```bash
# everything
dotnet test

# unit only, fast loop
dotnet test backend/tests/Marketplace.UnitTests

# integration, one area
dotnet test backend/tests/Marketplace.IntegrationTests --filter "FullyQualifiedName~AuthorizationIsolation"

# with coverage
dotnet test --collect:"XPlat Code Coverage"

# frontend gate
cd frontend/marketplace-web
npm run lint && npm run typecheck && npm run build
```

## 8. CI gates

| Workflow | Job | Fails on |
| --- | --- | --- |
| `backend.yml` | restore, build | compiler errors, warnings-as-errors |
| | unit tests | any failing test |
| | integration tests | any failing test |
| | `dotnet list package --vulnerable` | high/critical advisories |
| `frontend.yml` | `npm ci` | lockfile drift |
| | lint + typecheck | any error |
| | `npm run build` | build failure |

## 9. Definition of done for a feature

```text
□ Domain rules implemented in the Domain layer (not in a controller)
□ Validators written for every new request DTO
□ Seller/ownership scoping applied to every new query
□ Audit entry for the mutation
□ Notification for user-visible state changes
□ Unit tests for the calculation / state machine
□ Integration tests for the endpoint, including the negative authorization cases
□ Cache invalidation wired
□ Frontend loading + empty + error states
□ `dotnet test` and `npm run build` green
□ Documentation updated
```
