# Functional Requirements

> Numbered, testable statements. Every requirement carries an ID so that tests, commits and issue trackers can reference it.

## 1. Roles

| ID | Requirement |
| --- | --- |
| FR-1.1 | The system supports four roles: `SuperAdmin`, `Admin`, `Seller`, `Customer`. |
| FR-1.2 | A user may hold exactly one marketplace role; role changes take effect on the next token refresh. |
| FR-1.3 | `SuperAdmin` may perform every action an `Admin` may, plus manage other admins and platform-wide settings. |

## 2. Authentication (FR-AUTH)

| ID | Requirement |
| --- | --- |
| FR-2.1 | Anyone can register as a `Customer` with email, password, full name and phone. |
| FR-2.2 | Email must be unique across all users; duplicates return `409` with a field-level error. |
| FR-2.3 | Passwords require ≥ 8 characters with at least one upper-case letter, one lower-case letter and one digit. |
| FR-2.4 | Login returns a short-lived JWT access token (15 min) and a refresh token (30 days). |
| FR-2.5 | The refresh token is a single-use, rotating, hashed-at-rest token. |
| FR-2.6 | Presenting an already-rotated (revoked) refresh token revokes the whole token family and returns `401` — replay detection. |
| FR-2.7 | Logout revokes the presented refresh token and records the event in the audit log. |
| FR-2.8 | `GET /api/auth/me` returns the authenticated user with role, seller id (if any) and store name (if any). |
| FR-2.9 | Failed logins are rate limited per IP and per email, and are recorded without logging the attempted password. |
| FR-2.10 | Password reset and email-confirmation tokens are single-use with a 24-hour expiry. |

## 3. Authorization (FR-AUTHZ)

| ID | Requirement |
| --- | --- |
| FR-3.1 | Every endpoint is either anonymous, role-restricted, or authenticated; no endpoint relies on the UI to hide it. |
| FR-3.2 | A `Customer` calling any admin endpoint receives `403`. |
| FR-3.3 | A seller reading or mutating another seller's products, orders, coupons, reviews or inventory receives `403`. |
| FR-3.4 | Seller-scoped endpoints resolve the seller from the authenticated principal, never from the request body. |
| FR-3.5 | A customer reading or mutating another customer's order, cart, address or wishlist receives `403`. |
| FR-3.6 | Suspended sellers are blocked from all seller endpoints and their products are delisted. |

## 4. Seller lifecycle (FR-SEL)

| ID | Requirement |
| --- | --- |
| FR-4.1 | An authenticated customer may apply to become a seller, supplying store name, slug, description, phone, address, trade licence and bank details. |
| FR-4.2 | An applied seller has status `Pending` and cannot list products until approved. |
| FR-4.3 | An admin can approve, reject (with reason) or suspend (with reason) a seller. |
| FR-4.4 | Every status transition notifies the applicant and is written to the audit log. |
| FR-4.5 | Store slugs are unique, URL-safe and immutable after first publish. |
| FR-4.6 | A seller can edit store profile, branding, return policy and payout details but cannot change approval status. |
| FR-4.7 | Admins can list and filter sellers by status, and inspect a seller's full detail page. |

## 5. Categories (FR-CAT)

| ID | Requirement |
| --- | --- |
| FR-5.1 | Categories form a tree with a single parent, unlimited depth and an ordering index. |
| FR-5.2 | Each category has a unique slug and an optional description. |
| FR-5.3 | A category with active child products cannot be deleted; it can only be deactivated. |
| FR-5.4 | The public API returns the tree with product counts and a breadcrumb trail per node. |
| FR-5.5 | Only `Admin`/`SuperAdmin` may create, update, reorder or delete categories. |

## 6. Products (FR-PRD)

| ID | Requirement |
| --- | --- |
| FR-6.1 | A seller may only manage products that belong to their own seller. |
| FR-6.2 | A product requires name, slug, description, category, base price, stock and at least one image. |
| FR-6.3 | Slugs are unique across the marketplace and generated from the name when omitted. |
| FR-6.4 | A product supports 0..n images (ordered, one flagged primary) and 0..n variants. |
| FR-6.5 | A variant carries its own SKU, price, stock and option values (e.g. `Colour=Black`, `Size=M`). |
| FR-6.6 | Newly created products enter `PendingApproval` and are invisible publicly until an admin approves them. |
| FR-6.7 | Only `Published` and non-deleted products of active sellers appear in listings, search and the sitemap. |
| FR-6.8 | Public listing supports search over name, description, SKU, category and store; filters for price, rating, category, seller, availability and discount; and sorting by newest, price ↑, price ↓, rating and popularity. |
| FR-6.9 | Listing is server-side paginated with a default page size of 20 and a hard cap of 100. |
| FR-6.10 | Admins can approve, reject, feature, unfeature and soft-delete any product. |

## 7. Inventory (FR-INV)

| ID | Requirement |
| --- | --- |
| FR-7.1 | Each product variant owns an inventory record tracking available, reserved and sold quantities. |
| FR-7.2 | Reservations are created at checkout, carry an expiry (default 15 min) and are released on expiry. |
| FR-7.3 | Reserving more than the currently available quantity fails atomically with `409 insufficient_stock` — concurrent checkouts can never oversell. |
| FR-7.4 | Every quantity change writes an immutable inventory transaction (type, delta, before/after, reference, actor). |
| FR-7.5 | Sellers can adjust stock manually with a mandatory reason. |
| FR-7.6 | Stock falling to or below the variant threshold raises `InventoryLow` and notifies the seller. |
| FR-7.7 | Releasing a reservation restores availability exactly once, even if executed twice. |

## 8. Cart (FR-CART)

| ID | Requirement |
| --- | --- |
| FR-8.1 | A signed-in customer has exactly one active cart; guests may use an anonymous cart keyed by a cookie. |
| FR-8.2 | Adding an item stores the current unit price; prices are re-validated on every read and at checkout. |
| FR-8.3 | Quantity updates are clamped to the available stock. |
| FR-8.4 | Items are grouped by seller in the response and the UI. |
| FR-8.5 | Customers can save an item for later and move it back to the cart. |
| FR-8.6 | Clearing the cart requires explicit confirmation. |

## 9. Checkout & orders (FR-ORD)

| ID | Requirement |
| --- | --- |
| FR-9.1 | Checkout re-validates product price, availability, stock, coupon validity and the delivery address server-side. |
| FR-9.2 | The backend computes subtotal, discount, shipping, tax and grand total; client-submitted totals are ignored. |
| FR-9.3 | Checkout runs inside one database transaction: reserve → create order → split seller orders → create payment → clear cart. |
| FR-9.4 | A single marketplace order is created and split into one `SellerOrder` per distinct seller. |
| FR-9.5 | The customer sees one order with one number and one payment; each seller sees only their sub-order. |
| FR-9.6 | Order items snapshot name, image, unit price and variant at purchase time so later edits cannot rewrite history. |
| FR-9.7 | Order status follows a legal state machine; illegal transitions are rejected with `409`. |
| FR-9.8 | Customers may cancel only while the order is `Pending`, `Confirmed` or `Processing`; cancellation releases reservations. |
| FR-9.9 | Order detail shows a full tracking timeline with timestamps for each state. |
| FR-9.10 | Customers can list and filter their own orders by status and date, with pagination. |

## 10. Payments (FR-PAY)

| ID | Requirement |
| --- | --- |
| FR-10.1 | Payment providers are abstracted behind `IPaymentGateway`; at minimum `Mock` and `CashOnDelivery` ship enabled. |
| FR-10.2 | A payment belongs to an order and records amount, provider, provider reference, status and timestamps. |
| FR-10.3 | Gateway responses are persisted as immutable `PaymentTransaction` rows (requested / responded / verified). |
| FR-10.4 | Webhooks are accepted only when the HMAC signature over the raw body validates. |
| FR-10.5 | Webhook processing is idempotent: a repeated provider event id is acknowledged `200` without re-applying effects. |
| FR-10.6 | The amount and order referenced in a webhook must match the stored payment, otherwise it is rejected and logged. |
| FR-10.7 | `CashOnDelivery` orders are marked paid on delivery without a gateway call. |
| FR-10.8 | A successful payment releases the inventory reservation into `sold` and creates commissions. |

## 11. Refunds (FR-REF)

| ID | Requirement |
| --- | --- |
| FR-11.1 | A customer may request a refund for order items belonging to a delivered order, once per item. |
| FR-11.2 | A request requires a reason and free-text detail. |
| FR-11.3 | An admin may approve or reject a request; rejection requires a reason visible to the customer. |
| FR-11.4 | An approved refund calls the gateway, then returns stock to the seller and reverses the commission. |
| FR-11.5 | Duplicate refund requests for the same item are rejected with `409`. |
| FR-11.6 | Every refund transition notifies the customer and is audit-logged. |

## 12. Commissions (FR-COM)

| ID | Requirement |
| --- | --- |
| FR-12.1 | Each seller order captures the commission rate in force at checkout, as a historical snapshot. |
| FR-12.2 | Commission amounts are calculated per seller order from its own subtotal, never from the marketplace order total. |
| FR-12.3 | Seller earnings = seller-order subtotal − commission − seller-side shipping. |
| FR-12.4 | Commissions are created only after the payment succeeds, inside the same transaction. |
| FR-12.5 | Sellers can view a paginated earnings statement; admins can view total commission revenue over time. |

## 13. Reviews (FR-REV)

| ID | Requirement |
| --- | --- |
| FR-13.1 | Only the buyer of a delivered order item may review that item, once. |
| FR-13.2 | A review carries a 1..5 rating, a title and a body. |
| FR-13.3 | Product rating aggregates (average, count, per-star distribution) are recomputed on every write. |
| FR-13.4 | Admins can moderate (hide) a review; sellers can reply once per review. |
| FR-13.5 | Only visible reviews are returned publicly. |

## 14. Coupons (FR-CPN)

| ID | Requirement |
| --- | --- |
| FR-14.1 | A coupon is either percentage or fixed amount, with an optional minimum order value and maximum discount cap. |
| FR-14.2 | A coupon has an activation window, a global usage limit and a per-user usage limit. |
| FR-14.3 | A coupon may be restricted to one seller and/or a set of products. |
| FR-14.4 | Validation returns a structured result explaining why a coupon is not applicable. |
| FR-14.5 | Discount is applied only to eligible line items and never exceeds the maximum discount. |
| FR-14.6 | Global coupons are managed by admins; seller coupons are managed by the owning seller only. |

## 15. Notifications (FR-NOT)

| ID | Requirement |
| --- | --- |
| FR-15.1 | Notifications are created by domain events for both customers and sellers. |
| FR-15.2 | Each notification has a type, title, body, link, read state and creation time. |
| FR-15.3 | The API exposes unread count, a paginated list, mark-as-read and mark-all-as-read. |
| FR-15.4 | Unread notifications are pushed over SignalR; the bell badge updates without a page reload. |

## 16. Dashboards & analytics (FR-ANA)

| ID | Requirement |
| --- | --- |
| FR-16.1 | The seller dashboard shows total sales, today's sales, pending orders, product count, low stock, average rating and unanswered reviews. |
| FR-16.2 | The seller dashboard charts cover revenue, orders, top products and sales by category over a selectable period. |
| FR-16.3 | The admin dashboard shows customers, sellers, products, orders, revenue, pending sellers, pending products, open refunds and commission revenue. |
| FR-16.4 | Admin charts cover revenue, orders, seller growth, customer growth, category performance and refund rate. |
| FR-16.5 | The customer dashboard shows recent orders, wishlist count, saved addresses and unread notifications. |
| FR-16.6 | Admins can query the audit log with filters on actor, action, entity and date range, plus pagination. |

## 17. Storefront (FR-WEB)

| ID | Requirement |
| --- | --- |
| FR-17.1 | Public pages are server-rendered and indexable: home, product, category, store, product listing and search. |
| FR-17.2 | Product, category and store pages emit dynamic metadata, canonical URLs, Open Graph tags and JSON-LD structured data. |
| FR-17.3 | `sitemap.xml` lists public routes only; `robots.txt` disallows all dashboard, cart, checkout and account routes. |
| FR-17.4 | Product cards show image, name, store, rating, price, discount, stock state, wishlist and add-to-cart. |
| FR-17.5 | The cart groups items by seller and shows subtotal, shipping, discount and total. |
| FR-17.6 | Checkout is a stepped flow: address → delivery → payment → review → place order. |
| FR-17.7 | A product page shows gallery, variants, quantity, stock, seller, description, specifications, reviews and related products. |
| FR-17.8 | The UI supports light, dark and system themes, and is usable from 360 px to 1920 px. |
| FR-17.9 | Every major route renders a skeleton while loading and a recoverable error state on failure. |

## 18. Platform operations (FR-OPS)

| ID | Requirement |
| --- | --- |
| FR-18.1 | `GET /health` reports liveness; `GET /health/ready` reports database, cache and worker status. |
| FR-18.2 | Swagger/OpenAPI documents every endpoint in development. |
| FR-18.3 | Login, registration, checkout and webhook endpoints are rate limited. |
| FR-18.4 | Errors are returned as RFC 7807 `ProblemDetails` with a correlation id. |
| FR-18.5 | Secrets are supplied only through configuration/environment, never committed. |
