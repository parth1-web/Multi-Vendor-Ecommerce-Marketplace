/** Admin console endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type {
  AdminSummary,
  AdminUser,
  AdminUserPage,
  AuditPage,
  CategorySales,
  CommissionReportRow,
  GrowthPoint,
  InventoryReportRow,
  RefundAnalytics,
  RevenuePoint,
  SalesReportRow,
  SellerReportRow,
} from "@/types/admin";
import type { DateRange } from "@/types/seller";
import type { PagedResult } from "@/types/api";
import type { ProductStatus, ProductSummary } from "@/types/product";
import type { ProductRejectionReason } from "@/types/productAuthoring";
import type { Order, OrderPage, OrderStatus } from "@/types/order";
import type { UserRole } from "@/types/auth";

const withRange = (range: DateRange) => `range=${range}`;

export const adminApi = {
  summary: () => apiClient.get<AdminSummary>("/api/admin/analytics/summary").then(data => data.data),

  revenue: (range: DateRange) => apiClient.get<RevenuePoint[]>(`/api/admin/analytics/revenue?${withRange(range)}`).then(data => data.data),

  growth: (range: DateRange) => apiClient.get<GrowthPoint[]>(`/api/admin/analytics/growth?${withRange(range)}`).then(data => data.data),

  categoryPerformance: (range: DateRange) =>
    apiClient.get<CategorySales[]>(`/api/admin/analytics/category-performance?${withRange(range)}`).then(data => data.data),

  refunds: (range: DateRange) => apiClient.get<RefundAnalytics>(`/api/admin/analytics/refunds?${withRange(range)}`).then(data => data.data),

  /** The moderation queue: everything a reviewer has to decide on. */
  moderationQueue: (params: { page: number; pageSize?: number; status?: ProductStatus }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
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

  updateOrderStatus: (id: string, status: OrderStatus, note?: string | null) =>
    apiClient.put(`/api/admin/orders/${id}/status`, { status, note: note ?? null }).then(() => undefined),
};

export type { AdminUser };
