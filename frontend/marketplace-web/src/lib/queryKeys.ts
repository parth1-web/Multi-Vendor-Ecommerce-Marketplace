/**
 * Every query key in the application, in one file.
 *
 * A SignalR handler has to invalidate exactly the reads an event affects, and a key assembled
 * at the call site is one typo away from silently never matching. Keys live here so an event
 * and the component that renders the data are provably talking about the same thing.
 */

import type { ProductQuery } from "@/types/product";

export const queryKeys = {
  auth: {
    all: ["auth"] as const,
    me: () => [...queryKeys.auth.all, "me"] as const,
  },

  products: {
    all: ["products"] as const,
    lists: () => [...queryKeys.products.all, "list"] as const,
    list: (params: ProductQuery) => [...queryKeys.products.lists(), params] as const,
    details: () => [...queryKeys.products.all, "detail"] as const,
    detail: (idOrSlug: string) => [...queryKeys.products.details(), idOrSlug] as const,
    featured: () => [...queryKeys.products.all, "featured"] as const,
    bestSellers: () => [...queryKeys.products.all, "best-sellers"] as const,
    newArrivals: () => [...queryKeys.products.all, "new-arrivals"] as const,
  },

  categories: {
    all: ["categories"] as const,
    tree: () => [...queryKeys.categories.all, "tree"] as const,
    detail: (idOrSlug: string) => [...queryKeys.categories.all, "detail", idOrSlug] as const,
  },

  stores: {
    detail: (slug: string) => ["stores", slug] as const,
  },

  cart: {
    all: ["cart"] as const,
    detail: () => [...queryKeys.cart.all, "detail"] as const,
  },

  checkout: {
    quote: (params: unknown) => ["checkout", "quote", params] as const,
  },

  orders: {
    all: ["orders"] as const,
    lists: () => [...queryKeys.orders.all, "list"] as const,
    list: (params: unknown) => [...queryKeys.orders.lists(), params] as const,
    detail: (id: string) => [...queryKeys.orders.all, "detail", id] as const,
  },

  payments: {
    all: ["payments"] as const,
    mine: () => [...queryKeys.payments.all, "mine"] as const,
  },

  refunds: {
    all: ["refunds"] as const,
    mine: () => [...queryKeys.refunds.all, "mine"] as const,
  },

  wishlist: {
    all: ["wishlist"] as const,
    detail: () => [...queryKeys.wishlist.all, "detail"] as const,
  },

  addresses: {
    all: ["addresses"] as const,
    list: () => [...queryKeys.addresses.all, "list"] as const,
  },

  notifications: {
    all: ["notifications"] as const,
    list: (params: { page: number; unreadOnly: boolean }) => [...queryKeys.notifications.all, "list", params] as const,
    unreadCount: () => [...queryKeys.notifications.all, "unread-count"] as const,
  },

  inventory: {
    all: ["inventory"] as const,
    lists: () => [...queryKeys.inventory.all, "list"] as const,
    list: (params: unknown) => [...queryKeys.inventory.lists(), params] as const,
    lowStock: () => [...queryKeys.inventory.all, "low-stock"] as const,
    transactions: (variantId: string) => [...queryKeys.inventory.all, "transactions", variantId] as const,
  },

  coupons: {
    all: ["coupons"] as const,
    mine: () => [...queryKeys.coupons.all, "mine"] as const,
    public: () => [...queryKeys.coupons.all, "public"] as const,
  },

  reviews: {
    byProduct: (productId: string) => ["reviews", "product", productId] as const,
    seller: (params: unknown) => ["reviews", "seller", params] as const,
  },

  seller: {
    all: ["seller"] as const,
    products: (params: unknown) => [...queryKeys.seller.all, "products", params] as const,
    orders: (params: unknown) => [...queryKeys.seller.all, "orders", params] as const,
    summary: () => [...queryKeys.seller.all, "summary"] as const,
    revenue: (range: string) => [...queryKeys.seller.all, "revenue", range] as const,
    topProducts: (range: string) => [...queryKeys.seller.all, "top-products", range] as const,
    salesByCategory: (range: string) => [...queryKeys.seller.all, "sales-by-category", range] as const,
    commissions: (params: unknown) => [...queryKeys.seller.all, "commissions", params] as const,
    payouts: (params: unknown) => [...queryKeys.seller.all, "payouts", params] as const,
  },

  admin: {
    all: ["admin"] as const,
    summary: () => [...queryKeys.admin.all, "summary"] as const,
    revenue: (range: string) => [...queryKeys.admin.all, "revenue", range] as const,
    growth: (range: string) => [...queryKeys.admin.all, "growth", range] as const,
    users: (params: unknown) => [...queryKeys.admin.all, "users", params] as const,
    sellers: (params: unknown) => [...queryKeys.admin.all, "sellers", params] as const,
    auditLogs: (params: unknown) => [...queryKeys.admin.all, "audit-logs", params] as const,
  },

  /** Customer-facing summary, kept apart from the seller one so an event can target either. */
  customer: {
    summary: () => ["customer", "summary"] as const,
  },
} as const;
