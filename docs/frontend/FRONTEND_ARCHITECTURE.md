# Frontend Architecture

> Next.js 16 App Router Â· TypeScript strict Â· Bootstrap 5 + React-Bootstrap Â· TanStack Query Â· Zustand Â· Axios Â· React Hook Form + Zod Â· Recharts Â· Lucide React Â· `@microsoft/signalr`.

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
â”œâ”€â”€ app/
â”‚   â”œâ”€â”€ layout.tsx                 root: fonts, theme script, providers
â”‚   â”œâ”€â”€ (public)/
â”‚   â”‚   â”œâ”€â”€ layout.tsx             public shell: header, mega menu, footer
â”‚   â”‚   â”œâ”€â”€ page.tsx               homepage
â”‚   â”‚   â”œâ”€â”€ products/page.tsx      listing
â”‚   â”‚   â”œâ”€â”€ products/[slug]/       detail + generateMetadata
â”‚   â”‚   â”œâ”€â”€ categories/page.tsx
â”‚   â”‚   â”œâ”€â”€ categories/[slug]/page.tsx
â”‚   â”‚   â”œâ”€â”€ stores/[slug]/page.tsx
â”‚   â”‚   â”œâ”€â”€ search/page.tsx
â”‚   â”‚   â””â”€â”€ deals/page.tsx
â”‚   â”œâ”€â”€ (auth)/
â”‚   â”‚   â”œâ”€â”€ layout.tsx             centered card, split illustration
â”‚   â”‚   â”œâ”€â”€ login/page.tsx
â”‚   â”‚   â”œâ”€â”€ register/page.tsx
â”‚   â”‚   â””â”€â”€ forgot-password/page.tsx
â”‚   â”œâ”€â”€ (customer)/
â”‚   â”‚   â”œâ”€â”€ layout.tsx             requires session; account sidebar
â”‚   â”‚   â”œâ”€â”€ dashboard/page.tsx
â”‚   â”‚   â”œâ”€â”€ cart/page.tsx
â”‚   â”‚   â”œâ”€â”€ checkout/page.tsx
â”‚   â”‚   â”œâ”€â”€ orders/page.tsx
â”‚   â”‚   â”œâ”€â”€ orders/[id]/page.tsx
â”‚   â”‚   â”œâ”€â”€ wishlist/page.tsx
â”‚   â”‚   â”œâ”€â”€ profile/page.tsx
â”‚   â”‚   â”œâ”€â”€ addresses/page.tsx
â”‚   â”‚   â””â”€â”€ notifications/page.tsx
â”‚   â”œâ”€â”€ (seller)/seller/           sidebar shell
â”‚   â”‚   â”œâ”€â”€ dashboard/ products/ inventory/ orders/ reviews/
â”‚   â”‚   â”œâ”€â”€ coupons/ analytics/ store/ settings/
â”‚   â”œâ”€â”€ (admin)/admin/             dark sidebar shell
â”‚   â”‚   â”œâ”€â”€ dashboard/ users/ sellers/ products/ categories/ orders/
â”‚   â”‚   â”œâ”€â”€ payments/ refunds/ coupons/ commissions/ reports/
â”‚   â”‚   â”œâ”€â”€ audit-logs/ settings/
â”‚   â”œâ”€â”€ loading.tsx  error.tsx  not-found.tsx  global-error.tsx
â”‚   â”œâ”€â”€ sitemap.ts  robots.ts  manifest.ts
â”‚   â””â”€â”€ api/                       only webhooks/edge adapters, no business logic
â”œâ”€â”€ components/
â”‚   â”œâ”€â”€ ui/            Button, Card, Modal, DataTable, Pagination, Badge,
â”‚   â”‚                  Skeleton, EmptyState, ErrorState, Toast, Tabs,
â”‚   â”‚                  SearchInput, FilterPanel, ConfirmDialog, Avatar,
â”‚   â”‚                  StatCard, ProgressBar, Toggle, Tooltip
â”‚   â”œâ”€â”€ layout/        PublicHeader, PublicFooter, MegaMenu, MobileNav,
â”‚   â”‚                  DashboardShell, DashboardSidebar, DashboardTopbar,
â”‚   â”‚                  Breadcrumbs, NotificationBell, UserMenu, ThemeToggle
â”‚   â”œâ”€â”€ charts/        RevenueAreaChart, OrdersBarChart, DonutBreakdownChart,
â”‚   â”‚                  SparklineChart, TopProductsList, CategoryBarChart
â”‚   â”œâ”€â”€ products/      ProductCard, ProductGrid, ProductGallery, VariantPicker,
â”‚   â”‚                  QuantityStepper, ProductFilters, SortSelect, PriceRange,
â”‚   â”‚                  RatingStars, StockBadge, AddToCartButton, WishlistButton
â”‚   â”œâ”€â”€ cart/          CartLineItem, SellerGroup, CartSummary
â”‚   â”œâ”€â”€ checkout/      CheckoutSteps, AddressForm, DeliveryOption, PaymentOption,
â”‚   â”‚                  OrderReview, PlaceOrderButton
â”‚   â”œâ”€â”€ orders/        OrderTimeline, OrderStatusBadge, OrderCard, OrderItems
â”‚   â”œâ”€â”€ shared/        StorefrontRating, PriceDisplay, DiscountBadge, EmptyState...
â”‚   â””â”€â”€ seller/ admin/ customer/  area-specific composites
â”œâ”€â”€ features/          per domain: api hooks + views (products, cart, orders,
â”‚                      payments, sellers, reviews, coupons, notifications,
â”‚                      auth, analytics, reports, addresses, wishlist, inventory)
â”œâ”€â”€ api/               axiosClient, authApi, productApi, categoryApi, cartApi,
â”‚                      orderApi, paymentApi, sellerApi, reviewApi, couponApi,
â”‚                      notificationApi, reportApi, addressApi, analyticsApi
â”œâ”€â”€ hooks/             useDebounce, useMediaQuery, usePagination, useCopy,
â”‚                      useCountdown, useOnClickOutside, useDocumentTitle
â”œâ”€â”€ providers/         AppProviders, QueryProvider, AuthProvider,
â”‚                      SignalRProvider, ThemeProvider, ToastProvider
â”œâ”€â”€ store/             authStore, cartStore, uiStore, notificationStore
â”œâ”€â”€ types/             api.d.ts, product.ts, order.ts, ...
â”œâ”€â”€ schemas/           auth.ts, product.ts, checkout.ts, seller.ts, address.ts
â”œâ”€â”€ lib/               queryKeys.ts, format.ts, constants.ts, seo.ts
â”œâ”€â”€ styles/            globals.css, theme.css, variables.css, utilities.css
â””â”€â”€ public/            static assets
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
// 1. Server Component â€” direct fetch, no axios instance, no token
//    src/lib/serverApi.ts
export async function serverGet<T>(path: string, revalidate = 60): Promise<T> {
  const res = await fetch(`${process.env.NEXT_PUBLIC_API_URL}${path}`, {
    next: { revalidate, tags: [tagFor(path)] },
  });
  if (!res.ok) throw new ApiError(path, res.status);
  return res.json();
}

// 2. Client Component â€” TanStack Query over the api/ modules
//    src/features/products/api/useProducts.ts
export const useProducts = (params: ProductQuery) =>
  useQuery({
    queryKey: queryKeys.products.list(params),
    queryFn: ({ signal }) => productApi.list(params, signal),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });

// 3. Route Handlers â€” only for browser-facing concerns
//    app/api/webhooks/payment/route.ts  â†’ verifies + relays to the API
```

## 5. API client

`api/axiosClient.ts` owns everything transport-related:

- `baseURL` from `NEXT_PUBLIC_API_URL`.
- Request interceptor injects the in-memory access token and the correlation id.
- Response interceptor normalizes errors into a typed `ApiError { status, type, title, detail, fieldErrors, correlationId }`.
- A single-flight refresh: on the first `401`, one `POST /auth/refresh` runs while other 401s queue behind it; on success the queue replays, on failure the session is cleared and the user is routed to `/login?returnUrl=â€¦`.
- The refresh token rides in an `HttpOnly` cookie; the client never reads it.

## 6. Real-time integration

```text
SignalR event
    â†’ map to a TanStack Query key
    â†’ invalidateQueries / setQueryData
    â†’ components re-render with fresh server data
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

Connection state is surfaced subtly: an amber dot + "Reconnectingâ€¦" pill in the dashboard topbar. The app never blocks on it, and it forces a full refetch after reconnecting so nothing is missed.

## 7. SEO implementation

| Concern | Implementation |
| --- | --- |
| Titles | `generateMetadata` per route; template `"%s Â· {siteName}"` in the root layout |
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
| â‰¥ 1400 px | full nav, 4â€“5 column product grid, persistent filter rail, wide dashboards |
| 1200â€“1399 px | 3-column grid, condensed topbar |
| 992â€“1199 px | 2â€“3 column grid, collapsible filter drawer |
| 768â€“991 px | 2-column grid, off-canvas dashboard sidebar, hamburger nav |
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

Every list view renders one of four explicit states: `loading` â†’ `error` â†’ `empty` â†’ `data`. The `EmptyState` component always offers the next useful action (clear filters, add a product, start shopping) rather than a dead end.

## 12. Conventions

- Components: `PascalCase.tsx`, one component per file except tiny variant sets.
- Hooks: `useThing.ts`, returning `{ data, isLoading, error, ...actions }`.
- Types: `interface` for objects, `type` for unions; no `any`; `unknown` at boundaries.
- Import order enforced by ESLint: react â†’ next â†’ external â†’ internal â†’ styles.
- No direct `fetch` in components â€” always `api/*` or `lib/serverApi`.
- No business logic in components: pricing, totals and permissions come from the API.
