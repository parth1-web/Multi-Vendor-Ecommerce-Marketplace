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
    list: () => [...queryKeys.wishlist.all, "list"] as const,
  },


  addresses: {
    all: ["addresses"] as const,
    list: () => [...queryKeys.addresses.all, "list"] as const,
  },

  notifications: {
    all: ["notifications"] as const,
    list: (params: { page: number; unreadOnly: boolean; type?: string }) =>
      [...queryKeys.notifications.all, "list", params] as const,
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
    me: () => [...queryKeys.seller.all, "me"] as const,
    ownStore: () => [...queryKeys.seller.all, "own-store"] as const,
    products: (params: unknown) => [...queryKeys.seller.all, "products", params] as const,
    product: (id: string) => [...queryKeys.seller.all, "products", "detail", id] as const,
    inventory: (page: number) => [...queryKeys.seller.all, "inventory", page] as const,
    reviews: (params: unknown) => [...queryKeys.seller.all, "reviews", params] as const,
    orders: (params: unknown) => [...queryKeys.seller.all, "orders", params] as const,
    order: (id: string) => [...queryKeys.seller.all, "orders", "detail", id] as const,
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
    refunds: (range: string) => [...queryKeys.admin.all, "refunds", range] as const,
    /** The moderation queue, named apart from `products` so a decision invalidates only the queue. */
    moderation: (params: unknown) => [...queryKeys.admin.all, "moderation", params] as const,
    users: (params: unknown) => [...queryKeys.admin.all, "users", params] as const,
    sellers: (params: unknown) => [...queryKeys.admin.all, "sellers", params] as const,
    auditLogs: (params: unknown) => [...queryKeys.admin.all, "audit-logs", params] as const,
    orders: (params: unknown) => [...queryKeys.admin.all, "orders", params] as const,
    order: (id: string) => [...queryKeys.admin.all, "orders", "detail", id] as const,
    coupons: (params: unknown) => [...queryKeys.admin.all, "coupons", params] as const,
    salesReport: (range: string) => [...queryKeys.admin.all, "report-sales", range] as const,
    sellerReport: () => [...queryKeys.admin.all, "report-sellers"] as const,
    inventoryReport: () => [...queryKeys.admin.all, "report-inventory"] as const,
    commissionReport: (range: string) => [...queryKeys.admin.all, "report-commissions", range] as const,
  },


  /** Customer-facing summary, kept apart from the seller one so an event can target either. */
  customer: {
    summary: () => ["customer", "summary"] as const,
  },
} as const;
