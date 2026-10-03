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
    /**
     * Lists are gathered under one prefix so a mutation can say "every product list" without
     * naming the parameters each one was built with, and without also invalidating the detail
     * reads a modal is standing on.
     */
    productLists: () => [...queryKeys.seller.all, "product-lists"] as const,
    products: (params: unknown) => [...queryKeys.seller.productLists(), params] as const,
    productDetails: () => [...queryKeys.seller.all, "product"] as const,
    product: (id: string) => [...queryKeys.seller.productDetails(), id] as const,
    orderLists: () => [...queryKeys.seller.all, "order-lists"] as const,
    orders: (params: unknown) => [...queryKeys.seller.orderLists(), params] as const,
    orderDetails: () => [...queryKeys.seller.all, "order"] as const,
    order: (id: string) => [...queryKeys.seller.orderDetails(), id] as const,
    inventoryLists: () => [...queryKeys.seller.all, "inventory-lists"] as const,
    inventory: (params: unknown) => [...queryKeys.seller.inventoryLists(), params] as const,
    inventoryTransactions: (variantId: string) => [...queryKeys.seller.all, "inventory-transactions", variantId] as const,
    reviews: (params: unknown) => [...queryKeys.seller.all, "reviews", params] as const,
    summary: () => [...queryKeys.seller.all, "summary"] as const,
    revenue: (range: string) => [...queryKeys.seller.all, "revenue", range] as const,
    topProducts: (range: string) => [...queryKeys.seller.all, "top-products", range] as const,
    salesByCategory: (range: string) => [...queryKeys.seller.all, "sales-by-category", range] as const,
    orderBreakdown: () => [...queryKeys.seller.all, "order-breakdown"] as const,
    commissions: (params: unknown) => [...queryKeys.seller.all, "commissions", params] as const,
    payouts: (params: unknown) => [...queryKeys.seller.all, "payouts", params] as const,
  },

  /**
   * The seller's own discount codes.
   *
   * Separate from `seller` on purpose: these are the only coupon reads a seller performs, they are
   * invalidated by their own mutations, and nothing else in the workspace should be refetched
   * because a code was stopped. There is no detail key because the API has no coupon-by-id route.
   */
  sellerCoupons: {
    all: ["seller-coupons"] as const,
    lists: () => [...queryKeys.sellerCoupons.all, "list"] as const,
    list: (params: unknown) => [...queryKeys.sellerCoupons.lists(), params] as const,
  },

  admin: {
    all: ["admin"] as const,
    summary: () => [...queryKeys.admin.all, "summary"] as const,
    revenue: (range: string) => [...queryKeys.admin.all, "revenue", range] as const,
    growth: (range: string) => [...queryKeys.admin.all, "growth", range] as const,
    /** The moderation queue, named apart from `products` so a decision invalidates only the queue. */
    moderation: (params: unknown) => [...queryKeys.admin.all, "moderation", params] as const,
    users: (params: unknown) => [...queryKeys.admin.all, "users", params] as const,
    sellers: (params: unknown) => [...queryKeys.admin.all, "sellers", params] as const,
    auditLogs: (params: unknown) => [...queryKeys.admin.all, "audit-logs", params] as const,
    orders: (params: unknown) => [...queryKeys.admin.all, "orders", params] as const,
    order: (id: string) => [...queryKeys.admin.all, "orders", "detail", id] as const,
    coupons: (params: unknown) => [...queryKeys.admin.all, "coupons", params] as const,
    /**
     * The platform product list. The moderation queue is a filter over the same endpoint, so both
     * share one prefix: a stock adjustment or a category change refreshes the catalogue and the
     * queue together, because they are the same rows read differently.
     */
    products: (params: unknown) => [...queryKeys.admin.all, "products", params] as const,
    /**
     * Refunds, payments, categories and platform stock each get their own prefix rather than
     * living under `admin.all`, so a refund decision refetches refunds rather than the whole
     * console — and so a category write does not drag the seller report with it.
     */
    /**
     * Refund analytics for the overview, kept apart from the refund *list* below: one is an
     * aggregate over a period, the other is the queue an administrator works through. Sharing a
     * key would mean approving a refund refetching every chart on the dashboard.
     */
    refundAnalytics: (range: string) => [...queryKeys.admin.all, "refund-analytics", range] as const,
    /**
     * Category revenue for the overview's ranking.
     *
     * Its own prefix rather than the seller's `salesByCategory`, because the two are different
     * questions about different data — one is the marketplace, the other is one store's slice of
     * it — and sharing a key would let a seller's dashboard invalidate the admin overview.
     */
    categoryPerformance: (range: string) => [...queryKeys.admin.all, "category-performance", range] as const,
    /** The refund queue an administrator works through, and the single refund behind it. */
    refunds: (params: unknown) => [...queryKeys.admin.all, "refunds", "list", params] as const,
    refund: (id: string) => [...queryKeys.admin.all, "refunds", "detail", id] as const,
    payments: (params: unknown) => [...queryKeys.admin.all, "payments", "list", params] as const,
    payment: (id: string) => [...queryKeys.admin.all, "payments", "detail", id] as const,
    categories: () => [...queryKeys.admin.all, "categories"] as const,
    /**
     * The review moderation queue, and the single review behind it.
     *
     * Named `reviews` rather than folded into `moderation`, which is the *listing* approval queue:
     * hiding a review has nothing to do with approving a product, and sharing a key would make
     * every product decision refetch the review queue and the other way round.
     */
    reviews: (params: unknown) => [...queryKeys.admin.all, "reviews", "list", params] as const,
    review: (id: string) => [...queryKeys.admin.all, "reviews", "detail", id] as const,
    /** Platform stock, read through the shared inventory route with the seller predicate dropped. */
    platformInventory: (params: unknown) => [...queryKeys.admin.all, "platform-inventory", "list", params] as const,
    platformInventoryTransactions: (variantId: string) =>
      [...queryKeys.admin.all, "platform-inventory", "transactions", variantId] as const,
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
