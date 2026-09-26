# Non-Functional Requirements

## 1. Performance

| ID | Requirement | Target |
| --- | --- | --- |
| NFR-1.1 | Public page server-render (TTFB) | p95 < 600 ms warm |
| NFR-1.2 | Public catalogue API | p95 < 250 ms |
| NFR-1.3 | Authenticated dashboard API | p95 < 500 ms |
| NFR-1.4 | Checkout (validate + reserve + create order + payment) | p95 < 2 s |
| NFR-1.5 | Client bundle for a public product page (JS, gzipped) | < 160 kB |
| NFR-1.6 | Dashboard bundle | split by route; charting library only in dashboard chunks |
| NFR-1.7 | Client-side route transitions | no full page reload; streaming where possible |
| NFR-1.8 | Peak throughput | 250 concurrent marketplace users, 20 checkouts/s burst |

**Enforcement techniques**

- Server-side pagination everywhere; a hard cap of 100 rows per page.
- DTO projection with `AsNoTracking` for all read paths; no entity graph materialisation on listings.
- Composite indexes for every documented filter/sort combination (see [`DATABASE_DESIGN.md`](database/DATABASE_DESIGN.md)).
- `Include` only what is rendered; avoid N+1 by projecting seller/store names inline.
- Redis read-through caching for catalogue, category and store payloads with tagged invalidation.
- `next/image` with explicit `sizes` to avoid CLS; blur placeholders for the LCP image.
- Route-level code splitting; Recharts dynamically imported in dashboards only.

## 2. Scalability

| ID | Requirement |
| --- | --- |
| NFR-2.1 | The backend is stateless — any instance can serve any request; session state lives in Redis/PostgreSQL. |
| NFR-2.2 | Horizontal scaling requires no code change: sticky sessions are not required for REST. |
| NFR-2.3 | SignalR uses the Azure SignalR-compatible backplane abstraction so a Redis backplane can be added by configuration. |
| NFR-2.4 | Caches are node-local fallbacks only; the shared cache is Redis. |
| NFR-2.5 | Module boundaries allow individual modules to be extracted to services later. |
| NFR-2.6 | Background work is offloaded from request threads; no job runs inline during a request beyond enqueue. |

## 3. Reliability

| ID | Requirement |
| --- | --- |
| NFR-3.1 | Checkout, inventory reservation, order creation, payment state change, refund processing and commission creation are transactional. |
| NFR-3.2 | Payment webhooks are idempotent and replay-safe. |
| NFR-3.3 | Inventory can never be oversold under concurrency (conditional-UPDATE guard + concurrency token). |
| NFR-3.4 | All background jobs are idempotent and safe to retry. |
| NFR-3.5 | A failure inside a transaction leaves no partial order, payment or commission behind. |
| NFR-3.6 | The API degrades gracefully: if Redis is down the app still serves from PostgreSQL; if PostgreSQL is down `/health/ready` reports unhealthy. |
| NFR-3.7 | Real-time delivery loss is acceptable (the client refetches on reconnect); financial state is never SignalR-only. |
| NFR-3.8 | Error responses never leak stack traces, SQL or internal identifiers to the client. |

## 4. Security

| ID | Requirement |
| --- | --- |
| NFR-4.1 | Passwords stored as PBKDF2-HMAC-SHA256 (100 000 iterations, 128-bit salt) or BCrypt — never plaintext, never reversible. |
| NFR-4.2 | Access tokens expire in ≤ 30 minutes; refresh tokens in ≤ 30 days and are rotated on every use. |
| NFR-4.3 | Refresh tokens are stored as SHA-256 hashes; a replayed token revokes the entire family. |
| NFR-4.4 | Long-lived refresh tokens are never kept in `localStorage`, `sessionStorage` or Zustand persistence. |
| NFR-4.5 | Seller isolation is enforced server-side on every seller-scoped query via an explicit seller predicate. |
| NFR-4.6 | All input is validated: FluentValidation on the server, Zod in the browser. |
| NFR-4.7 | CORS uses an explicit origin allow-list, never `*` with credentials. |
| NFR-4.8 | Security headers applied: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Strict-Transport-Security`. |
| NFR-4.9 | Rate limiting on auth, checkout, review and webhook endpoints. |
| NFR-4.10 | No secret is committed to the repository; all values come from configuration or environment variables. |
| NFR-4.11 | Payment webhooks require a valid HMAC signature and are rejected otherwise. |
| NFR-4.12 | Logs never contain passwords, tokens, refresh tokens, webhook secrets or full card data. |
| NFR-4.13 | Ownership is verified for customer resources (order, address, wishlist) as well as seller resources. |

## 5. Usability & accessibility

| ID | Requirement |
| --- | --- |
| NFR-5.1 | Semantic HTML landmarks (`header`, `nav`, `main`, `aside`, `footer`) on every page. |
| NFR-5.2 | All interactive elements reachable and operable by keyboard with a visible focus ring. |
| NFR-5.3 | Every form control has a programmatically associated label. |
| NFR-5.4 | Status is never conveyed by colour alone — badges always carry text. |
| NFR-5.5 | Body text meets WCAG AA contrast (≥ 4.5:1) in both light and dark themes. |
| NFR-5.6 | `prefers-reduced-motion` disables chart animation and non-essential transitions. |
| NFR-5.7 | Modals trap focus, close on `Escape`, and restore focus to the trigger. |
| NFR-5.8 | Every async view has a loading, empty, error and success state. |
| NFR-5.9 | Text remains readable at 200 % zoom. |

## 6. Compatibility & responsiveness

| ID | Requirement |
| --- | --- |
| NFR-6.1 | Verified viewports: 1920×1080, 1440×900, 1366×768, 1024×768, 768×1024, 430×932, 390×844, 360×800. |
| NFR-6.2 | Browsers: last two versions of Chrome, Edge, Firefox and Safari; iOS Safari 16+. |
| NFR-6.3 | Mobile provides off-canvas navigation and filters, a single-column product detail, a stacked checkout and card-style tables. |
| NFR-6.4 | Touch targets are ≥ 44×44 px on mobile. |
| NFR-6.5 | No horizontal scrolling at 360 px on any route. |

## 7. SEO

| ID | Requirement |
| --- | --- |
| NFR-7.1 | Every public page has a unique `<title>` (≤ 60 chars) and meta description (≤ 160 chars). |
| NFR-7.2 | Canonical URLs are absolute and self-referencing. |
| NFR-7.3 | Product pages emit `Product` JSON-LD (name, image, description, sku, brand, offers, aggregateRating). |
| NFR-7.4 | Store pages emit `Organization`/`Store` JSON-LD. |
| NFR-7.5 | Breadcrumb pages emit `BreadcrumbList` JSON-LD. |
| NFR-7.6 | `sitemap.xml` contains only indexable public URLs; `robots.ts` disallows every private route. |
| NFR-7.7 | Open Graph and Twitter card metadata on all public pages. |
| NFR-7.8 | Images carry meaningful alt text derived from product/store data. |

## 8. Observability & operations

| ID | Requirement |
| --- | --- |
| NFR-8.1 | Structured JSON logs with a correlation id per request. |
| NFR-8.2 | Health endpoints expose database, cache and worker status. |
| NFR-8.3 | Business-critical actions (auth, seller status, product approval, order, payment, refund, commission, coupon, admin) write an audit record with actor, action, entity, before/after and IP. |
| NFR-8.4 | Payment, webhook, order and job events are logged at `Information` or above with the relevant entity id. |
| NFR-8.5 | OpenAPI is generated from code so documentation cannot drift. |
| NFR-8.6 | CI runs backend build + tests and frontend lint + build on every push and pull request. |

## 9. Maintainability

| ID | Requirement |
| --- | --- |
| NFR-9.1 | Four-layer dependency rule; Domain has zero package references (asserted by a test). |
| NFR-9.2 | Controllers contain no business logic beyond binding, calling a use case and mapping the result. |
| NFR-9.3 | Business rules are unit-testable without I/O — `IClock` is injected instead of `DateTimeOffset.UtcNow`. |
| NFR-9.4 | The frontend keeps a strict split: `api/` (HTTP), `features/` (hooks + views), `components/` (presentational), `store/` (Zustand), `types/`, `schemas/`. |
| NFR-9.5 | No duplicate business logic in Next.js Route Handlers. |
| NFR-9.6 | Public names, folder layout and commit messages follow the conventions documented in [`DEVELOPMENT.md`](DEVELOPMENT.md). |
