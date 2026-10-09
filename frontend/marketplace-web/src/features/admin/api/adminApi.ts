/** Admin console endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type {
  AdminCategory,
  AdminSummary,
  AdminUser,
  AdminUserPage,
  AuditActionName,
  AuditPage,
  CategorySales,
  CommissionReportRow,
  CreateCategoryRequest,
  GrowthPoint,
  InventoryReportPage,
  InventoryReportQuery,
  Payment,
  PaymentPage,
  PaymentState,
  Refund,
  RefundAnalytics,
  RefundPage,
  RefundReviewAction,
  RefundState,
  SalesExportResult,
  RevenuePoint,
  SalesReportRow,
  SellerReportPage,
  SellerReportQuery,
  UpdateCategoryRequest,
} from "@/types/admin";
import type { DateRange } from "@/types/seller";
import type { PagedResult } from "@/types/api";
import type { ProductStatus, ProductSummary } from "@/types/product";
import type { ProductRejectionReason } from "@/types/productAuthoring";
import type { Order, OrderPage, OrderStatus } from "@/types/order";
import type { Coupon, CouponPage, CouponStatus, CreateCouponRequest, UpdateCouponRequest } from "@/types/coupon";
import type { InventoryItem, InventoryTransaction } from "@/types/seller";
import type { ModerationReview, ModerationReviewPage, ModerationReviewQuery } from "@/types/review";
import type { UserRole } from "@/types/auth";

const withRange = (range: DateRange) => `range=${range}`;

export const adminApi = {
  summary: () => apiClient.get<AdminSummary>("/api/admin/analytics/summary").then(data => data.data),

  revenue: (range: DateRange) => apiClient.get<RevenuePoint[]>(`/api/admin/analytics/revenue?${withRange(range)}`).then(data => data.data),

  growth: (range: DateRange) => apiClient.get<GrowthPoint[]>(`/api/admin/analytics/growth?${withRange(range)}`).then(data => data.data),

  categoryPerformance: (range: DateRange) =>
    apiClient.get<CategorySales[]>(`/api/admin/analytics/category-performance?${withRange(range)}`).then(data => data.data),

  /**
   * Refund *analytics* for a period — counts and amounts, not rows.
   *
   * Named apart from `refunds` below because the two are different things: this one aggregates,
   * and the other is the list an administrator works through.
   */
  refundAnalytics: (range: DateRange) =>
    apiClient.get<RefundAnalytics>(`/api/admin/analytics/refunds?${withRange(range)}`).then(data => data.data),

  /**
   * Every listing on the marketplace, with the moderation fields a reviewer needs.
   *
   * `search` matches a product's name, short description, full description or any variant SKU.
   * `status` accepts the API's own `ProductStatus` names; an unrecognised one is silently ignored
   * server-side, which is why the caller only ever sends values from the enum.
   *
   * Only `page`, `pageSize`, `status` and `search` exist. There is no seller filter, no category
   * filter, no price range and no sort on this route, so none is offered — filtering a page of
   * twenty in the browser and calling it a seller filter would quietly hide rows.
   */
  moderationQueue: (params: { page: number; pageSize?: number; status?: ProductStatus; search?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<PagedResult<ProductSummary>>(`/api/admin/products?${search.toString()}`).then(data => data.data);
  },

  reviewProduct: (id: string, approve: boolean, reason: ProductRejectionReason, note: string) =>
    apiClient.put(`/api/admin/products/${id}/approval`, { approve, reason, note: note || null }).then(() => undefined),

  setFeatured: (id: string, featured: boolean) =>
    apiClient.put(`/api/admin/products/${id}/featured?value=${featured}`, {}).then(() => undefined),

  sellers: (params: { page: number; pageSize?: number; status?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
    }

    return apiClient
      .get<PagedResult<{ id: string; businessName: string; userName: string; email: string; status: string; productCount: number; sellerOrderCount: number; totalRevenue: number; storeSlug: string | null; appliedAt: string; approvedAt: string | null }>>(
        `/api/sellers?${search.toString()}`,
      )
      .then(data => data.data);
  },

  setSellerStatus: (id: string, status: string, reason: string | null) =>
    apiClient.put(`/api/sellers/${id}/status`, { status, reason }).then(() => undefined),

  users: (params: { page: number; pageSize?: number; search?: string; role?: UserRole }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.search) {
      search.set("search", params.search);
    }

    if (params.role) {
      search.set("role", params.role);
    }

    return apiClient.get<AdminUserPage>(`/api/admin/users?${search.toString()}`).then(data => data.data);
  },

  setUserRole: (id: string, role: UserRole) => apiClient.put(`/api/admin/users/${id}/role`, { role }).then(() => undefined),

  setUserActive: (id: string, isActive: boolean) => apiClient.put(`/api/admin/users/${id}/status`, { isActive }).then(() => undefined),

  /**
   * The audit log, filtered server-side and paginated server-side.
   *
   * Every filter the route accepts is here: `action` (an `AuditAction` name), `entityType`,
   * `entityId`, `actorId`, `from`, `to` and `search`. `search` is not a free-text search over
   * every field — the API matches the actor's email and the entity's name only, so a term that
   * appears in neither returns nothing even when it is in the row's payload.
   *
   * `from`/`to` are sent as ISO instants rather than dates, because `to` is inclusive server-side:
   * a bare `2026-10-03` would mean midnight at the *start* of that day and silently exclude
   * everything after it. Callers pass the end of the day they mean.
   *
   * There is no export. The API has no audit export route, and building a CSV in the browser from
   * one page of results would be a file that looks like the log and is not.
   */
  auditLogs: (params: {
    page: number;
    pageSize?: number;
    action?: AuditActionName;
    entityType?: string;
    entityId?: string;
    actorId?: string;
    from?: string;
    to?: string;
    search?: string;
  }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 25) });

    if (params.action) {
      search.set("action", params.action);
    }

    if (params.entityType) {
      search.set("entityType", params.entityType);
    }

    if (params.entityId) {
      search.set("entityId", params.entityId);
    }

    if (params.actorId) {
      search.set("actorId", params.actorId);
    }

    if (params.from) {
      search.set("from", params.from);
    }

    if (params.to) {
      search.set("to", params.to);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<AuditPage>(`/api/admin/audit-logs?${search.toString()}`).then(data => data.data);
  },

/**
 * Sales per period, one row per bucket, gap-filled by the API.
 *
 * `range` is the only parameter the route accepts — it is a preset name, and the server resolves it
 * to a window and a bucket size (daily for the short ranges, weekly for 90 days, monthly for a
 * year). There is no `from`/`to` here even though `/api/admin/analytics/revenue` has them, so a
 * custom window is not offered rather than offered and ignored.
 *
 * Every money column is a real aggregate now: discounts, tax, shipping and refunds are summed from
 * what the orders in the bucket recorded at checkout, and `netRevenue` is gross less discounts
 * less refunds. Cancelled orders are excluded by the server.
 */
salesReport: (range: DateRange) => apiClient.get<SalesReportRow[]>(`/api/admin/reports/sales?${withRange(range)}`).then(data => data.data),

/**
 * Sellers, ranked by gross revenue, paged and filtered by the server.
 *
 * `range` is optional and its absence means lifetime totals — the behaviour this report had before
 * it grew filters. Supplying one scopes the order aggregates to that window; the products and
 * rating columns are platform-wide either way, because neither is a period figure.
 *
 * `search` matches the seller's business name or their store's name, case-insensitively.
 */
sellerReport: (params: SellerReportQuery) => {
  const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

  if (params.search) {
    search.set("search", params.search);
  }

  if (params.status) {
    search.set("status", params.status);
  }

  if (params.range) {
    search.set("range", params.range);
  }

  return apiClient.get<SellerReportPage>(`/api/admin/reports/sellers?${search.toString()}`).then(data => data.data);
},

/**
 * Stock per variant, lowest available first, paged by the server.
 *
 * `lowStockOnly` means sellable quantity at or below the variant's own threshold — the same
 * definition the seller inventory screen uses — and `outOfStockOnly` means no available quantity.
 * `search` matches the product name, the SKU or the store, case-insensitively. `sellerId`
 * narrows to one store.
 */
inventoryReport: (params: InventoryReportQuery) => {
  const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

  if (params.search) {
    search.set("search", params.search);
  }

  if (params.sellerId) {
    search.set("sellerId", params.sellerId);
  }

  if (params.lowStockOnly) {
    search.set("lowStockOnly", "true");
  }

  if (params.outOfStockOnly) {
    search.set("outOfStockOnly", "true");
  }

  return apiClient.get<InventoryReportPage>(`/api/admin/reports/inventory?${search.toString()}`).then(data => data.data);
},

/**
 * Commission earned per seller within the range, ranked by amount.
 *
 * Every column is scoped to the range, including the payout pair: those count completed payouts
 * *created* inside the window, which is the only period question a payout record can answer.
 */
commissionReport: (range: DateRange) => apiClient.get<CommissionReportRow[]>(`/api/admin/reports/commissions?${withRange(range)}`).then(data => data.data),

/**
 * The sales CSV, fetched rather than linked.
 *
 * Three reasons it is not a plain `<a download>`:
 *
 * - The button has to say whether the export worked, and a navigation cannot report that.
 * - The response's `X-Export-Row-Limit` header says how many rows the server will write before it
 *   stops. A file with exactly that many rows may have been cut short, and the reader is entitled to
 *   know rather than to assume completeness.
 * - The whole export is streamed, so the browser holds the file as it arrives instead of the
 *   server holding every order in memory to build one string.
 *
 * The content itself is **one row per order**, including cancelled ones — so it does not match the
 * sales table on screen, which measures revenue and leaves cancelled orders out. Only the range is
 * shared between them, and both resolve it through the same server-side code.
 */
exportSalesCsv: async (range: DateRange): Promise<SalesExportResult> => {
  const response = await apiClient.get<string>(`/api/admin/reports/export/sales.csv?${withRange(range)}`, {
    responseType: "text",
    transformResponse: [(data: string) => data],
  });

  const limitHeader = response.headers["x-export-row-limit"];
  const rowLimit = Number(limitHeader ?? 0);
  const lines = response.data.split("\n").filter(line => line.trim() !== "");

  return {
    fileName: `sales-${range}-${new Date().toISOString().slice(0, 10)}.csv`,
    // The header line is not a data row.
    rowCount: Math.max(0, lines.length - 1),
    rowLimit,
    text: response.data,
  };
},

  /**
   * Every order on the marketplace, across all sellers.
   *
   * This is the whole marketplace's order book rather than one seller's half of it: an order here
   * is what a shopper bought from several stores at once, so a support question about a missing
   * item starts here rather than in any single storefront.
   */
  orders: (params: { page: number; pageSize?: number; status?: OrderStatus; search?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<OrderPage>(`/api/admin/orders?${search.toString()}`).then(data => data.data);
  },

  order: (id: string) => apiClient.get<Order>(`/api/admin/orders/${id}`).then(data => data.data),

  /**
   * Moves an order, and every seller sub-order that can legally follow it.
   *
   * The legal steps are the server's, not this file's: an illegal move comes back as 409 with the
   * transition it refused. The note is written to the order's history and defaults server-side
   * when omitted.
   */
  updateOrderStatus: (id: string, status: OrderStatus, note?: string | null) =>
    apiClient.put(`/api/admin/orders/${id}/status`, { status, note: note ?? null }).then(() => undefined),

  /**
   * Platform-wide stock.
   *
   * The same route the seller workspace uses: it is `SellerOrAdmin`, and the service drops its
   * seller predicate for an admin, so one call returns every seller's rows. There is no seller
   * filter in the contract and no seller field in the response, so the store is joined client-side
   * from the product list rather than asked for.
   */
  inventory: (params: { page: number; pageSize?: number; search?: string; lowStockOnly?: boolean; outOfStockOnly?: boolean }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.search) {
      search.set("search", params.search);
    }

    if (params.lowStockOnly) {
      search.set("lowStockOnly", "true");
    }

    if (params.outOfStockOnly) {
      search.set("outOfStockOnly", "true");
    }

    return apiClient
      .get<PagedResult<InventoryItem>>(`/api/inventory?${search.toString()}`)
      .then(data => data.data);
  },

  inventoryTransactions: (variantId: string, take = 50) =>
    apiClient.get<InventoryTransaction[]>(`/api/inventory/${variantId}/transactions?take=${take}`).then(data => data.data),

  adjustStock: (variantId: string, delta: number, reason: string) =>
    apiClient.put<InventoryItem>(`/api/inventory/${variantId}`, { delta, reason }).then(data => data.data),

  /**
   * The discount codes on the marketplace.
   *
   * A code with no way to make one is a code that only exists in the seed, so this is where a
   * promotion is actually created. The rules — when it applies, how much it takes off, how many
   * times — are all the server's; this screen collects them and shows what came back.
   */
  coupons: (params: { page: number; pageSize?: number; status?: CouponStatus; search?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<CouponPage>(`/api/coupons?${search.toString()}`).then(data => data.data);
  },

  createCoupon: (request: CreateCouponRequest) => apiClient.post<Coupon>("/api/coupons", request).then(data => data.data),

  updateCoupon: (id: string, request: UpdateCouponRequest) => apiClient.put<Coupon>(`/api/coupons/${id}`, request).then(data => data.data),

  /** Stops a code applying. The row stays, because its usage is part of the record. */
  stopCoupon: (id: string) => apiClient.delete(`/api/coupons/${id}`).then(() => undefined),

  /**
   * Every payment on the marketplace.
   *
   * `/api/payments`, not `/api/admin/payments` — the admin list is the same route the customer's
   * own list would use, gated by `AdminOnly`. Only `page`, `pageSize`, `status`, `orderId` and
   * `search` exist, and `search` matches the transaction reference alone.
   */
  payments: (params: { page: number; pageSize?: number; status?: PaymentState; orderId?: string; search?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
    }

    if (params.orderId) {
      search.set("orderId", params.orderId);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<PaymentPage>(`/api/payments?${search.toString()}`).then(data => data.data);
  },

  /**
   * One payment, with the fields the list projection leaves empty.
   *
   * The list hard-codes `orderNumber` to an empty string and returns no transaction ledger, so
   * this is the only read that can show which order a payment belongs to or what the gateway
   * reported. It is deliberately fetched on demand rather than for every row on the page.
   */
  payment: (id: string) => apiClient.get<Payment>(`/api/payments/${id}`).then(data => data.data),

  /**
   * Every refund on the marketplace — `/api/refunds/all`, because `/api/refunds` is the
   * customer's own list and returns only their rows.
   */
  refunds: (params: { page: number; pageSize?: number; status?: RefundState; orderId?: string; search?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
    }

    if (params.orderId) {
      search.set("orderId", params.orderId);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<RefundPage>(`/api/refunds/all?${search.toString()}`).then(data => data.data);
  },

  refund: (id: string) => apiClient.get<Refund>(`/api/refunds/${id}`).then(data => data.data),

  /**
   * Approving a refund runs the whole chain synchronously: the gateway is called, stock is put
   * back, commissions are reversed and the refund is marked completed before the response arrives.
   * There is no separate approve-then-process action to chain afterwards.
   */
  reviewRefund: (id: string, action: RefundReviewAction, note: string) =>
    apiClient.put<Refund>(`/api/refunds/${id}/status`, { action, note: note || null }).then(data => data.data),

  /**
   * The category tree, inactive categories included.
   *
   * `includeInactive` is the only way to see a deactivated category at all — the default read
   * hides them — so an admin managing the catalogue always asks for them.
   */
  categories: () => apiClient.get<AdminCategory[]>("/api/categories?includeInactive=true").then(data => data.data),

  createCategory: (request: CreateCategoryRequest) =>
    apiClient.post<AdminCategory>("/api/categories", request).then(data => data.data),

  updateCategory: (id: string, request: UpdateCategoryRequest) =>
    apiClient.put<AdminCategory>(`/api/categories/${id}`, request).then(data => data.data),

  /**
   * Soft-deletes a category, and only when it holds no products and has no children. The API
   * refuses with 422 in both cases, and the caller is expected to explain that rather than
   * pretending a cascade happened.
   */
  deleteCategory: (id: string) => apiClient.delete(`/api/categories/${id}`).then(() => undefined),

  /**
   * Every review on the marketplace, hidden ones included.
   *
   * Hidden reviews are in the default list rather than behind a filter because finding one again is
   * the whole job: a moderator who hid something has to be able to see that they did, and why.
   * `visibility` is sent as `"true"`/`"false"` strings because the API reads it as a nullable
   * boolean, and omitting it means "every review".
   *
   * This is the only place a review can be listed across sellers. The seller's own list is scoped
   * to their products and the public one hides moderated reviews, so neither can answer "has
   * anybody complained about this product".
   */
  reviews: (params: ModerationReviewQuery) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.visibility) {
      search.set("visibility", params.visibility);
    }

    if (params.rating) {
      search.set("rating", String(params.rating));
    }

    if (params.search) {
      search.set("search", params.search);
    }

    if (params.productId) {
      search.set("productId", params.productId);
    }

    return apiClient.get<ModerationReviewPage>(`/api/admin/reviews?${search.toString()}`).then(data => data.data);
  },

  review: (id: string) => apiClient.get<ModerationReview>(`/api/admin/reviews/${id}`).then(data => data.data),

  /**
   * Hides a review, or puts it back.
   *
   * The note travels with the decision and is stored on the review, so the reason is still there
   * to whoever opens it next. The review text is never touched: moderation changes whether a
   * review is shown, not what it says.
   */
  setReviewVisibility: (id: string, isVisible: boolean, note?: string | null) =>
    apiClient.put(`/api/reviews/${id}/visibility`, { isVisible, note: note || null }).then(data => data.data),
};

export type { AdminUser };
