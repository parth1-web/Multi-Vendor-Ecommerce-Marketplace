/** Seller dashboard endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type {
  CategorySales,
  CommissionPage,
  DateRange,
  InventoryPage,
  OrderStatusCount,
  PayoutPage,
  RevenuePoint,
  SellerOrderPage,
  SellerProductPage,
  SellerSummary,
  TopProduct,
} from "@/types/seller";
import type { CreateProductRequest, UpdateProductRequest } from "@/types/productAuthoring";

export const sellerApi = {
  summary: () => apiClient.get<SellerSummary>("/api/seller/analytics/summary").then(data => data.data),

  revenue: (range: DateRange) =>
    apiClient.get<RevenuePoint[]>(`/api/seller/analytics/revenue?range=${range}`).then(data => data.data),

  topProducts: (range: DateRange, take = 5) =>
    apiClient.get<TopProduct[]>(`/api/seller/analytics/top-products?range=${range}&take=${take}`).then(data => data.data),

  salesByCategory: (range: DateRange) =>
    apiClient.get<CategorySales[]>(`/api/seller/analytics/sales-by-category?range=${range}`).then(data => data.data),

  orderBreakdown: () => apiClient.get<OrderStatusCount[]>("/api/seller/analytics/orders").then(data => data.data),

  products: (params: { page: number; pageSize?: number; search?: string; status?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.search) {
      search.set("search", params.search);
    }

    if (params.status) {
      search.set("status", params.status);
    }

    return apiClient.get<SellerProductPage>(`/api/seller/products?${search.toString()}`).then(data => data.data);
  },

  createProduct: (request: CreateProductRequest) => apiClient.post("/api/seller/products", request).then(data => data.data),

  updateProduct: (id: string, request: UpdateProductRequest) => apiClient.put(`/api/seller/products/${id}`, request).then(data => data.data),

  deleteProduct: (id: string) => apiClient.delete(`/api/seller/products/${id}`).then(() => undefined),

  /**
   * Puts a draft in front of a moderator. A listing is not live until somebody approves it,
   * which is the only reason a shopper can trust that what is for sale exists.
   */
  submitForApproval: (id: string) => apiClient.post(`/api/seller/products/${id}/submit`, {}).then(() => undefined),

  orders: (params: { page: number; pageSize?: number; status?: string; search?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize ?? 20) });

    if (params.status) {
      search.set("status", params.status);
    }

    if (params.search) {
      search.set("search", params.search);
    }

    return apiClient.get<SellerOrderPage>(`/api/seller/orders?${search.toString()}`).then(data => data.data);
  },

  order: (id: string) => apiClient.get(`/api/seller/orders/${id}`).then(data => data.data),

  /**
   * Moves a sub-order along. The seller owns fulfilment for their own part of an order, which
   * is the whole point of splitting it per seller in the first place.
   */
  updateOrderStatus: (id: string, body: { status: string; note?: string | null; carrierName?: string | null; trackingNumber?: string | null }) =>
    apiClient.put(`/api/seller/orders/${id}/status`, body).then(data => data.data),

  inventory: (params: { page: number; lowStockOnly?: boolean }) => {
    const path = params.lowStockOnly ? "/api/inventory/low-stock" : "/api/inventory";
    return apiClient.get<InventoryPage>(`${path}?page=${params.page}&pageSize=20`).then(data => data.data);
  },

  commissions: (params: { page: number; status?: string }) => {
    const search = new URLSearchParams({ page: String(params.page), pageSize: "20" });

    if (params.status) {
      search.set("status", params.status);
    }

    return apiClient.get<CommissionPage>(`/api/seller/commissions?${search.toString()}`).then(data => data.data);
  },

  payouts: (params: { page: number }) =>
    apiClient.get<PayoutPage>(`/api/seller/payouts?page=${params.page}&pageSize=20`).then(data => data.data),
};


