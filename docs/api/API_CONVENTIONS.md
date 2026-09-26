# API Conventions

> Base URL: `/api` · Content type: `application/json` · Auth: `Authorization: Bearer <accessToken>` · All timestamps ISO-8601 UTC.

## 1. Envelope

Single-item responses return the resource directly. Collections return a consistent envelope so the client never has to guess the shape:

```jsonc
// GET /api/products?page=1&pageSize=20
{
  "items": [ /* ... */ ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 250,
  "totalPages": 13,
  "hasPrevious": false,
  "hasNext": true
}
```

Mutations that return a created resource use `201 Created` with a `Location` header.

## 2. Errors — RFC 7807

Every failure returns `application/problem+json`:

```json
{
  "type": "https://errors.marketplace.dev/insufficient-stock",
  "title": "Insufficient stock",
  "status": 409,
  "detail": "Only 1 unit of 'AeroLux Wireless Headphones' is available.",
  "instance": "/api/checkout",
  "correlationId": "0HN7Q1P2K9A3",
  "errors": {
    "items[2].quantity": ["Requested 3, available 1."]
  }
}
```

| Status | Used for |
| --- | --- |
| `400` | Validation failure (field errors in `errors`) |
| `401` | Missing, malformed or expired token; refresh-token replay |
| `403` | Authenticated but not permitted (role, ownership, seller isolation) |
| `404` | Resource missing, or deliberately hidden from the caller |
| `409` | State conflict: insufficient stock, illegal order transition, duplicate coupon usage, duplicate refund |
| `422` | Business rule violated that is not a state conflict |
| `429` | Rate limit exceeded (`Retry-After` header) |
| `500` | Unhandled error — internal detail is logged, never returned |

`correlationId` is echoed in the response header and in every log line for the request, so a user-reported error can be traced end to end.

## 3. Authentication flow

```text
POST /api/auth/login
  → 200 { accessToken, refreshToken, expiresInSeconds, user }
     · accessToken  : JWT, 15 min, sent to the API on every call
     · refreshToken : opaque, single-use, rotated, HttpOnly cookie
                      (falls back to a returned value for non-browser clients)

POST /api/auth/refresh
  → rotates the token, returns a new pair
  → if the presented token was already rotated → 401 + revoke the whole family

POST /api/auth/logout
  → revokes the presented refresh token, clears the cookie

GET  /api/auth/me
  → { id, email, firstName, lastName, role, sellerId, storeName, ... }
```

**Token claims**

```json
{
  "sub": "user-guid",
  "email": "seller@marketplace.dev",
  "role": "Seller",
  "sid": "seller-guid",
  "jti": "unique-token-id",
  "iss": "marketplace-api",
  "aud": "marketplace-web",
  "iat": 1772000000,
  "exp": 1772000900
}
```

`sid` (seller id) is only present for sellers — the service layer reads the seller scope from the token, never from the request body.

## 4. Authorization

| Policy | Grants |
| --- | --- |
| `AdminOnly` | `Admin`, `SuperAdmin` |
| `SuperAdminOnly` | `SuperAdmin` |
| `SellerOnly` | `Seller` with a non-`Suspended` status |
| `CustomerOnly` | `Customer` |
| `ActiveSellerOnly` | `Seller` with status `Active` (listing products, creating coupons) |
| `SelfOrAdmin` | ownership verified in the service against the token subject |
| `SellerResource` | `Seller` whose token `sid` matches the route/resource seller |

A `Seller` hitting a `Customer` endpoint gets `403`, a `Customer` hitting an `AdminOnly` endpoint gets `403`, and seller A hitting seller B's resource gets `403` — never `404`, so ownership failures stay debuggable without leaking data (the payload body is always `ProblemDetails`).

## 5. Query-string conventions

| Parameter | Type | Notes |
| --- | --- | --- |
| `page` | int ≥ 1 | default `1` |
| `pageSize` | int 1..100 | default `20` |
| `search` | string | trimmed, max 200 chars |
| `sort` | enum | `newest`, `price_asc`, `price_desc`, `rating`, `popular`, `name` |
| `from` / `to` | ISO date | inclusive range for date-filtered endpoints |
| `minPrice` / `maxPrice` | decimal | both optional; `minPrice > maxPrice` → `400` |
| `minRating` | 1..5 | |
| `status` | enum or csv | comma-separated multi-value filters |
| `categoryId` / `sellerId` | guid | |
| `inStock` | bool | |

Unknown query parameters are ignored rather than rejected, which keeps the API forwards-compatible.

## 6. Idempotency

`POST /api/orders`, `POST /api/payments` and `POST /api/payments/webhook` accept an optional `Idempotency-Key` header. Repeating a request with the same key returns the original result with `Idempotency-Replayed: true` instead of creating a duplicate.

Webhook idempotency does not depend on the client: uniqueness is enforced by the database on `(provider, provider_event_id)`.

## 7. Rate limits

| Scope | Limit |
| --- | --- |
| `/api/auth/login` | 10 per minute per IP, 5 per minute per email |
| `/api/auth/register` | 5 per hour per IP |
| `/api/checkout` | 20 per minute per user |
| `/api/products/{id}/reviews` | 5 per hour per user |
| `/api/payments/webhook` | 300 per minute per provider IP |
| global | 3000 requests per minute per IP |

Exceeding a limit returns `429` with `Retry-After`.

## 8. Versioning

The API is versioned by media type, not URL: `Accept: application/vnd.marketplace.v1+json`. The default `application/json` is treated as `v1`. A breaking change introduces `v2` and both run side by side during migration.

## 9. Endpoint catalogue

```text
AUTH        POST   /api/auth/register
            POST   /api/auth/login
            POST   /api/auth/refresh
            POST   /api/auth/logout
            GET    /api/auth/me
            PATCH  /api/auth/me
            POST   /api/auth/forgot-password
            POST   /api/auth/reset-password
            POST   /api/auth/confirm-email

SELLERS     POST   /api/sellers/apply
            GET    /api/sellers                     (admin, filterable)
            GET    /api/sellers/me                  (seller)
            GET    /api/sellers/{id}                (admin or owning seller)
            PUT    /api/sellers/{id}                (admin or owning seller)
            PUT    /api/sellers/{id}/status         (admin)
            GET    /api/sellers/{id}/store
            PUT    /api/sellers/{id}/store

CATEGORIES  GET    /api/categories                  (public tree)
            GET    /api/categories/{idOrSlug}
            POST   /api/categories                  (admin)
            PUT    /api/categories/{id}             (admin)
            DELETE /api/categories/{id}             (admin)
            PUT    /api/categories/reorder          (admin)

PRODUCTS    GET    /api/products                    (public, filterable)
            GET    /api/products/featured
            GET    /api/products/{id}
            GET    /api/products/slug/{slug}        (public)
            GET    /api/sellers/{sellerId}/products
            POST   /api/products                    (seller)
            PUT    /api/products/{id}               (owner seller or admin)
            DELETE /api/products/{id}               (owner seller or admin)
            PUT    /api/products/{id}/approval      (admin)
            PUT    /api/products/{id}/featured      (admin)
            POST   /api/products/{id}/images
            DELETE /api/products/{id}/images/{imageId}
            POST   /api/products/{id}/variants
            PUT    /api/products/{id}/variants/{variantId}
            DELETE /api/products/{id}/variants/{variantId}

INVENTORY   GET    /api/inventory                   (seller, own only)
            GET    /api/inventory/low-stock         (seller)
            PUT    /api/inventory/{variantId}       (seller adjust)
            GET    /api/inventory/{variantId}/transactions

CART        GET    /api/cart
            POST   /api/cart/items
            PUT    /api/cart/items/{itemId}
            DELETE /api/cart/items/{itemId}
            PUT    /api/cart/items/{itemId}/save-for-later
            DELETE /api/cart

WISHLIST    GET    /api/wishlist
            POST   /api/wishlist
            DELETE /api/wishlist/{productId}
            DELETE /api/wishlist

CHECKOUT    POST   /api/checkout/quote             (totals preview, no writes)
            POST   /api/checkout                   (reserve + order + payment)

ORDERS      GET    /api/orders                      (customer, own only)
            GET    /api/orders/{id}                 (customer, seller or admin)
            PUT    /api/orders/{id}/status          (seller sub-order or admin)
            POST   /api/orders/{id}/cancel
            GET    /api/seller-orders               (seller)
            GET    /api/seller-orders/{id}
            GET    /api/admin/orders                (admin)

PAYMENTS    GET    /api/payments/{id}
            POST   /api/payments/{id}/verify
            POST   /api/payments/webhook            (unauthenticated, HMAC)
            GET    /api/admin/payments             (admin)

REFUNDS     POST   /api/refunds
            GET    /api/refunds                     (customer own / admin all)
            GET    /api/refunds/{id}
            PUT    /api/refunds/{id}/status         (admin)

COMMISSIONS GET    /api/seller/commissions          (seller)
            GET    /api/admin/commissions           (admin)

REVIEWS     GET    /api/products/{productId}/reviews
            POST   /api/products/{productId}/reviews
            PUT    /api/reviews/{id}                (author)
            DELETE /api/reviews/{id}                (author or admin)
            PUT    /api/reviews/{id}/visibility     (admin)
            POST   /api/reviews/{id}/reply          (seller)
            GET    /api/seller/reviews              (seller)

COUPONS     GET    /api/coupons                     (admin all / seller own)
            POST   /api/coupons
            PUT    /api/coupons/{id}
            DELETE /api/coupons/{id}
            POST   /api/coupons/validate
            GET    /api/coupons/public             (active, eligible codes)

NOTIFICATIONS GET  /api/notifications
            GET    /api/notifications/unread-count
            PUT    /api/notifications/{id}/read
            PUT    /api/notifications/read-all
            DELETE /api/notifications/{id}

ADDRESSES   GET    /api/addresses
            POST   /api/addresses
            PUT    /api/addresses/{id}
            DELETE /api/addresses/{id}
            PUT    /api/addresses/{id}/default

ANALYTICS   GET    /api/seller/analytics/summary
            GET    /api/seller/analytics/revenue
            GET    /api/seller/analytics/top-products
            GET    /api/seller/analytics/sales-by-category
            GET    /api/admin/analytics/summary
            GET    /api/admin/analytics/revenue
            GET    /api/admin/analytics/growth
            GET    /api/admin/analytics/category-performance
            GET    /api/admin/analytics/refunds
            GET    /api/customer/analytics/summary

ADMIN       GET    /api/admin/users
            PUT    /api/admin/users/{id}/role
            PUT    /api/admin/users/{id}/status
            GET    /api/admin/audit-logs
            GET    /api/admin/settings
            PUT    /api/admin/settings

REPORTS     GET    /api/admin/reports/sales
            GET    /api/admin/reports/sellers
            GET    /api/admin/reports/inventory
            GET    /api/admin/reports/commissions
            GET    /api/reports/export              (csv)

SYSTEM      GET    /health
            GET    /health/ready
```

## 10. SignalR hub

`/hubs/marketplace` — see [`ARCHITECTURE.md`](../architecture/ARCHITECTURE.md) §6.

Client → server: `JoinOrder(orderId)`, `LeaveOrder(orderId)`, `Ping`.

Server → client: `NotificationCreated`, `OrderUpdated`, `OrderStatusChanged`, `PaymentUpdated`, `RefundUpdated`, `InventoryLow`, `SellerStatusChanged`, `NewReview`, `PlatformMetricsUpdated`, `CartUpdated`.
