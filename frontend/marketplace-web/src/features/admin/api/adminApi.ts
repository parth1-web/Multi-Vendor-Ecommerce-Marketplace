/** Admin console endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type {
  AdminCategory,
  AdminSummary,
  AdminUser,
  AdminUserPage,
  AuditPage,
  CategorySales,
  CommissionReportRow,
  CreateCategoryRequest,
  GrowthPoint,
  InventoryReportRow,
  Payment,
  PaymentPage,
  PaymentState,
  Refund,
  RefundAnalytics,
  RefundPage,
  RefundReviewAction,
  RefundState,
  RevenuePoint,
  SalesReportRow,
  SellerReportRow,
  UpdateCategoryRequest,
} from "@/types/admin";
import type { DateRange } from "@/types/seller";
import type { PagedResult } from "@/types/api";
import type { ProductStatus, ProductSummary } from "@/types/product";
import type { ProductRejectionReason } from "@/types/productAuthoring";
import type { Order, OrderPage, OrderStatus } from "@/types/order";
import type { Coupon, CouponPage, CouponStatus, CreateCouponRequest, UpdateCouponRequest } from "@/types/coupon";
import type { InventoryItem, InventoryTransaction } from "@/types/seller";
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

  auditLogs: (params: { page: number; pageSize?: number; action?: string; search?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 25) });

    if (params.action) {
      search.set("action", params.action);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<AuditPage>(`/api/admin/audit-logs?${search.toString()}`).then(data => data.data);
  },

  salesReport: (range: DateRange) => apiClient.get<SalesReportRow[]>(`/api/admin/reports/sales?${withRange(range)}`).then(data => data.data),

  sellerReport: () => apiClient.get<SellerReportRow[]>("/api/admin/reports/sellers").then(data => data.data),

  inventoryReport: () => apiClient.get<InventoryReportRow[]>("/api/admin/reports/inventory").then(data => data.data),

  commissionReport: (range: DateRange) => apiClient.get<CommissionReportRow[]>(`/api/admin/reports/commissions?${withRange(range)}`).then(data => data.data),

  /** A direct link to the CSV, so the browser downloads it rather than the page navigating to it. */
  salesCsvUrl: (range: DateRange) => `${apiClient.defaults.baseURL}/api/admin/reports/export/sales.csv?${withRange(range)}`,

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
};

export type { AdminUser };
