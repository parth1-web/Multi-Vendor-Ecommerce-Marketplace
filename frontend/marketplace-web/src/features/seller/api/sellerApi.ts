/** Seller dashboard endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type {
  AdjustStockRequest,
  CategorySales,
  CommissionPage,
  DateRange,
  InventoryItem,
  InventoryPage,
  InventoryTransaction,
  OrderStatusCount,
  PayoutPage,
  RevenuePoint,
  SellerIdentity,
  SellerOrderDetail,
  SellerOrderPage,
  SellerOrderStatus,
  SellerProductPage,
  SellerSummary,
  TopProduct,
  UpdateStoreRequest,
} from "@/types/seller";
import type {
  CreateImageInput,
  CreateProductRequest,
  CreateVariantInput,
  SellerProductDetail,
  UpdateProductRequest,
} from "@/types/productAuthoring";
import type { StoreProfile } from "@/types/store";

export const sellerApi = {
  /**
   * The signed-in seller's own identity: status, commission rate and store handle. Scoped by the
   * token, so there is no id to pass and no other seller to ask for.
   */
  me: () => apiClient.get<SellerIdentity>("/api/sellers/me").then(data => data.data),

  /**
   * The seller's own storefront profile, with one page of its products attached. The products
   * ride along whether the settings form wants them or not, so callers that only need the
   * profile fields ignore that page rather than fetching it twice.
   */
  ownStore: () => apiClient.get<StoreProfile>("/api/sellers/me/store").then(data => data.data),

  updateOwnStore: (request: UpdateStoreRequest) =>
    apiClient.put<StoreProfile>("/api/sellers/me/store", request).then(data => data.data),

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
   * A seller's own listing, in full: the moderation fields the public detail deliberately omits.
   * Scoped to the signed-in seller by the API, so there is no "which seller" to pass.
   */
  product: (id: string) => apiClient.get<SellerProductDetail>(`/api/seller/products/${id}`).then(data => data.data),

  /**
   * The listing's pictures, variants and specifications, each one separately.
   *
   * They are separate operations with separate rules on the server, so they are separate calls
   * here too, and none of them is part of saving a listing's details.
   */
  addProductImage: (id: string, body: CreateImageInput) =>
    apiClient.post(`/api/seller/products/${id}/images`, body).then(data => data.data),

  deleteProductImage: (id: string, imageId: string) =>
    apiClient.delete(`/api/seller/products/${id}/images/${imageId}`).then(() => undefined),

  addProductVariant: (id: string, body: CreateVariantInput) =>
    apiClient.post(`/api/seller/products/${id}/variants`, body).then(data => data.data),

  deleteProductVariant: (id: string, variantId: string) =>
    apiClient.delete(`/api/seller/products/${id}/variants/${variantId}`).then(() => undefined),

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

  /**
   * One of this seller's orders, in full: the items, where they are going, and the progress.
   *
   * Scoped to the signed-in seller by the API, and a seller asking for another store's order id
   * gets the same "not found" as a wrong one rather than a refusal that would confirm it exists.
   */
  order: (id: string) => apiClient.get<SellerOrderDetail>(`/api/seller/orders/${id}`).then(data => data.data),

  /**
   * Moves a sub-order along. The seller owns fulfilment for their own part of an order, which
   * is the whole point of splitting it per seller in the first place.
   */
  updateOrderStatus: (id: string, body: { status: SellerOrderStatus; note?: string | null; carrierName?: string | null; trackingNumber?: string | null }) =>
    apiClient.put(`/api/seller/orders/${id}/status`, body).then(data => data.data),

/**
   * A page of this seller's stock.
   *
   * One endpoint does the filtering: `lowStockOnly`, `outOfStockOnly`, `search` (product name or
   * variant SKU) and `productId` are all parameters on the paged list. There is a separate
   * `/api/inventory/low-stock` route, but it answers with a bare array capped at 100 rows rather
   * than a page, so reading it as a page is how a filtered stock list ends up throwing.
   */
  inventory: (params: {
    page: number;
    pageSize?: number;
    search?: string;
    lowStockOnly?: boolean;
    outOfStockOnly?: boolean;
    productId?: string;
  }) => {
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

    if (params.productId) {
      search.set("productId", params.productId);
    }

    return apiClient.get<InventoryPage>(`/api/inventory?${search.toString()}`).then(data => data.data);
  },

  /**
   * Moves stock by a signed amount and records why.
   *
   * The reason is required by the server on every adjustment, so it is required here too: a
   * ledger entry that cannot be explained is an entry nobody can trust at stock-take time.
   * The response is the row as it now stands, which is what the list shows afterwards.
   */
  adjustStock: (variantId: string, request: AdjustStockRequest) =>
    apiClient.put<InventoryItem>(`/api/inventory/${variantId}`, request).then(data => data.data),

  /** The level at or below which the variant counts as low, in units of sellable stock. */
  setStockThreshold: (variantId: string, lowStockThreshold: number) =>
    apiClient.put<void>(`/api/inventory/${variantId}/threshold`, { lowStockThreshold }).then(() => undefined),

  /** The movement ledger for one variant, newest first. Empty when it has never moved. */
  inventoryTransactions: (variantId: string, take = 50) =>
    apiClient
      .get<InventoryTransaction[]>(`/api/inventory/${variantId}/transactions?take=${take}`)
      .then(data => data.data),

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


