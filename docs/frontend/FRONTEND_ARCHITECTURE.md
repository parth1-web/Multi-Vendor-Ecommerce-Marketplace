# Frontend Architecture

> Next.js 15 App Router · TypeScript strict · Bootstrap 5 + React-Bootstrap · TanStack Query · Zustand · Axios · React Hook Form + Zod · Recharts · Lucide React · `@microsoft/signalr`.

## 1. Rendering strategy

The rule is simple: **start on the server, go client only when interaction demands it.**

| Route | Rendering | Reason |
| --- | --- | --- |
| `/` | Server | SEO, no interactivity needed beyond carousels |
| `/products` | Server shell + client filter island | Initial results indexable; filters are interactive |
| `/products/[slug]` | Server | SEO-critical; JSON-LD; only the buy box is client |
| `/categories/[slug]` | Server | SEO + static-friendly listing |
| `/stores/[slug]` | Server | SEO + store metadata |
| `/search` | Server + client filters | Query string drives the fetch |
| `/login`, `/register`, `/forgot-password` | Client | form interactivity, no SEO value |
| `/cart`, `/checkout` | Client | cart is live state, checkout is a wizard |
| `/orders`, `/wishlist`, `/profile`, `/addresses`, `/notifications` | Client (data) | authenticated, private, no SEO |
| `/seller/**`, `/admin/**` | Client | authenticated dashboards with Recharts |

Concrete rules:

- A `"use client"` directive must name a *reason*. The project's ESLint config flags client files that import neither state, effects, handlers nor a client-only library.
- The client boundary is pushed as deep as possible: `ProductCard` is a server component; `AddToCartButton` inside it is a 20-line client component.
- Recharts, SignalR and React-Bootstrap interactive widgets are loaded with `next/dynamic` (`ssr: false`) so the public bundle stays lean.

## 2. Directory layout

```text
frontend/marketplace-web/src/
├── app/
│   ├── layout.tsx                 root: fonts, theme script, providers
│   ├── (public)/
│   │   ├── layout.tsx             public shell: header, mega menu, footer
│   │   ├── page.tsx               homepage
│   │   ├── products/page.tsx      listing
│   │   ├── products/[slug]/       detail + generateMetadata
│   │   ├── categories/page.tsx
│   │   ├── categories/[slug]/page.tsx
│   │   ├── stores/[slug]/page.tsx
│   │   ├── search/page.tsx
│   │   └── deals/page.tsx
│   ├── (auth)/
│   │   ├── layout.tsx             centered card, split illustration
│   │   ├── login/page.tsx
│   │   ├── register/page.tsx
│   │   └── forgot-password/page.tsx
│   ├── (customer)/
│   │   ├── layout.tsx             requires session; account sidebar
│   │   ├── dashboard/page.tsx
│   │   ├── cart/page.tsx
│   │   ├── checkout/page.tsx
│   │   ├── orders/page.tsx
│   │   ├── orders/[id]/page.tsx
│   │   ├── wishlist/page.tsx
│   │   ├── profile/page.tsx
│   │   ├── addresses/page.tsx
│   │   └── notifications/page.tsx
│   ├── (seller)/seller/           sidebar shell
│   │   ├── dashboard/ products/ inventory/ orders/ reviews/
│   │   ├── coupons/ analytics/ store/ settings/
│   ├── (admin)/admin/             dark sidebar shell
│   │   ├── dashboard/ users/ sellers/ products/ categories/ orders/
│   │   ├── payments/ refunds/ coupons/ commissions/ reports/
│   │   ├── audit-logs/ settings/
│   ├── loading.tsx  error.tsx  not-found.tsx  global-error.tsx
│   ├── sitemap.ts  robots.ts  manifest.ts
│   └── api/                       only webhooks/edge adapters, no business logic
├── components/
│   ├── ui/            Button, Card, Modal, DataTable, Pagination, Badge,
│   │                  Skeleton, EmptyState, ErrorState, Toast, Tabs,
│   │                  SearchInput, FilterPanel, ConfirmDialog, Avatar,
│   │                  StatCard, ProgressBar, Toggle, Tooltip
│   ├── layout/        PublicHeader, PublicFooter, MegaMenu, MobileNav,
│   │                  DashboardShell, DashboardSidebar, DashboardTopbar,
│   │                  Breadcrumbs, NotificationBell, UserMenu, ThemeToggle
│   ├── charts/        RevenueAreaChart, OrdersBarChart, DonutBreakdownChart,
│   │                  SparklineChart, TopProductsList, CategoryBarChart
│   ├── products/      ProductCard, ProductGrid, ProductGallery, VariantPicker,
│   │                  QuantityStepper, ProductFilters, SortSelect, PriceRange,
│   │                  RatingStars, StockBadge, AddToCartButton, WishlistButton
│   ├── cart/          CartLineItem, SellerGroup, CartSummary
│   ├── checkout/      CheckoutSteps, AddressForm, DeliveryOption, PaymentOption,
│   │                  OrderReview, PlaceOrderButton
│   ├── orders/        OrderTimeline, OrderStatusBadge, OrderCard, OrderItems
│   ├── shared/        StorefrontRating, PriceDisplay, DiscountBadge, EmptyState...
│   └── seller/ admin/ customer/  area-specific composites
├── features/          per domain: api hooks + views (products, cart, orders,
│                      payments, sellers, reviews, coupons, notifications,
│                      auth, analytics, reports, addresses, wishlist, inventory)
├── api/               axiosClient, authApi, productApi, categoryApi, cartApi,
│                      orderApi, paymentApi, sellerApi, reviewApi, couponApi,
│                      notificationApi, reportApi, addressApi, analyticsApi
├── hooks/             useDebounce, useMediaQuery, usePagination, useCopy,
│                      useCountdown, useOnClickOutside, useDocumentTitle
├── providers/         AppProviders, QueryProvider, AuthProvider,
│                      SignalRProvider, ThemeProvider, ToastProvider
├── store/             authStore, cartStore, uiStore, notificationStore
├── types/             api.d.ts, product.ts, order.ts, ...
├── schemas/           auth.ts, product.ts, checkout.ts, seller.ts, address.ts
├── lib/               queryKeys.ts, format.ts, constants.ts, seo.ts
├── styles/            globals.css, theme.css, variables.css, utilities.css
└── public/            static assets
```

## 3. State model

```text
Server state    TanStack Query   products, categories, cart, orders, notifications,
                                 sellers, reviews, coupons, reports, analytics
UI state        Zustand         theme, sidebar collapsed, mobile nav open,
                                 active dashboard range, checkout draft, toasts
Auth session    AuthProvider    user + roles, hydrated from /api/auth/me,
                                 access token kept in a module singleton
Form state      React Hook Form local to the form; never global
```

Non-negotiables:

- Zustand never stores server data. If a value comes from the API, it lives in the query cache.
- Query keys are centralised in `lib/queryKeys.ts` so a SignalR handler can invalidate exactly the right key.
- Persisted Zustand slices (`theme`, `ui`) are the only ones using `persist`; nothing sensitive is ever persisted.

## 4. Data-fetching layers

```ts
// 1. Server Component — direct fetch, no axios instance, no token
//    src/lib/serverApi.ts
export async function serverGet<T>(path: string, revalidate = 60): Promise<T> {
  const res = await fetch(`${process.env.NEXT_PUBLIC_API_URL}${path}`, {
    next: { revalidate, tags: [tagFor(path)] },
  });
  if (!res.ok) throw new ApiError(path, res.status);
  return res.json();
}

// 2. Client Component — TanStack Query over the api/ modules
//    src/features/products/api/useProducts.ts
export const useProducts = (params: ProductQuery) =>
  useQuery({
    queryKey: queryKeys.products.list(params),
    queryFn: ({ signal }) => productApi.list(params, signal),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });

// 3. Route Handlers — only for browser-facing concerns
//    app/api/webhooks/payment/route.ts  → verifies + relays to the API
```

## 5. API client

`api/axiosClient.ts` owns everything transport-related:

- `baseURL` from `NEXT_PUBLIC_API_URL`.
- Request interceptor injects the in-memory access token and the correlation id.
- Response interceptor normalizes errors into a typed `ApiError { status, type, title, detail, fieldErrors, correlationId }`.
- A single-flight refresh: on the first `401`, one `POST /auth/refresh` runs while other 401s queue behind it; on success the queue replays, on failure the session is cleared and the user is routed to `/login?returnUrl=…`.
- The refresh token rides in an `HttpOnly` cookie; the client never reads it.

## 6. Real-time integration

```text
SignalR event
    → map to a TanStack Query key
    → invalidateQueries / setQueryData
    → components re-render with fresh server data
```

```ts
// providers/SignalRProvider.tsx
hub.on('OrderUpdated', (order: OrderDto) => {
  queryClient.setQueryData(queryKeys.orders.detail(order.id), order);
  queryClient.invalidateQueries({ queryKey: queryKeys.orders.lists() });
  queryClient.invalidateQueries({ queryKey: queryKeys.cart.detail() });
});

hub.on('NotificationCreated', (n: NotificationDto) => {
  queryClient.invalidateQueries({ queryKey: queryKeys.notifications.unreadCount() });
  toast.info(n.title, n.body);
});
```

Connection state is surfaced subtly: an amber dot + "Reconnecting…" pill in the dashboard topbar. The app never blocks on it, and it forces a full refetch after reconnecting so nothing is missed.

## 7. SEO implementation

| Concern | Implementation |
| --- | --- |
| Titles | `generateMetadata` per route; template `"%s · {siteName}"` in the root layout |
| Canonical | `alternates.canonical` with `NEXT_PUBLIC_SITE_URL` |
| Open Graph / Twitter | Shared builder in `lib/seo.ts` |
| Product JSON-LD | `Product` + `AggregateRating` + `Offer` + `Brand` from the product DTO |
| Breadcrumb JSON-LD | `BreadcrumbList` on product, category and store pages |
| Store JSON-LD | `Organization` with `AggregateRating` |
| Sitemap | `app/sitemap.ts` with `revalidate = 3600`; static routes + all published products, categories and stores |
| Robots | `app/robots.ts` disallowing `/dashboard`, `/cart`, `/checkout`, `/orders`, `/wishlist`, `/profile`, `/addresses`, `/notifications`, `/seller`, `/admin`, `/api` |
| Images | `next/image` with per-breakpoint `sizes`, meaningful `alt` from product/store data, `priority` only on the LCP image |

## 8. Accessibility implementation

- Landmarks on every layout; one `h1` per page; heading order never skipped.
- Skip-to-content link as the first focusable element.
- All icon-only buttons carry `aria-label`; badges never rely on colour alone.
- Modals: focus trap, `Escape` to close, focus restored to the trigger, `aria-modal`.
- Forms: real `<label>`s, `aria-invalid` + `aria-describedby` on errors, errors announced with `role="alert"`.
- `prefers-reduced-motion` disables Recharts animation and CSS transitions.
- Visible `:focus-visible` ring on every interactive element.

## 9. Responsive implementation

| Width | Behaviour |
| --- | --- |
| ≥ 1400 px | full nav, 4–5 column product grid, persistent filter rail, wide dashboards |
| 1200–1399 px | 3-column grid, condensed topbar |
| 992–1199 px | 2–3 column grid, collapsible filter drawer |
| 768–991 px | 2-column grid, off-canvas dashboard sidebar, hamburger nav |
| < 768 px | 2-column compact grid, off-canvas filters, stacked checkout, card-style tables, bottom cart bar |

Tables use a `responsiveTable` wrapper that converts to stacked definition cards below 768 px rather than forcing horizontal scroll.

## 10. Performance budget

| Metric | Budget |
| --- | --- |
| Public product page JS (gzip) | < 160 kB |
| Dashboard route JS (gzip) | < 320 kB incl. Recharts, loaded only on dashboards |
| CSS (gzip) | < 40 kB |
| LCP | < 2.0 s on 4G |
| CLS | < 0.05 |

Enforced by: server-first rendering, `next/image` with `sizes`, `dynamic` imports for charts and heavy widgets, no barrel-file imports of large libraries, Bootstrap Sass compiled once with only the components actually used.

## 11. Error, loading and empty states

```text
loading.tsx        skeleton matching the final layout (no spinner-only screens)
error.tsx          human message + retry + "go back" + support hint
not-found.tsx      helpful 404 with search + category shortcuts
global-error.tsx   root fallback that also survives a root-layout crash
```

Every list view renders one of four explicit states: `loading` → `error` → `empty` → `data`. The `EmptyState` component always offers the next useful action (clear filters, add a product, start shopping) rather than a dead end.

## 12. Conventions

- Components: `PascalCase.tsx`, one component per file except tiny variant sets.
- Hooks: `useThing.ts`, returning `{ data, isLoading, error, ...actions }`.
- Types: `interface` for objects, `type` for unions; no `any`; `unknown` at boundaries.
- Import order enforced by ESLint: react → next → external → internal → styles.
- No direct `fetch` in components — always `api/*` or `lib/serverApi`.
- No business logic in components: pricing, totals and permissions come from the API.
