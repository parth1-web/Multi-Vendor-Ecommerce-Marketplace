/** Shared constants. Anything a business rule fixes lives here rather than inline in a component. */

export const APP_NAME = "Marketplace";
export const CURRENCY = "USD";

/**
 * Stale times, chosen per screen rather than globally: a dashboard and a product page have
 * different costs of being wrong.
 */
export const STALE_TIME = {
  /** A catalogue page: cheap to refetch, and freshness is what a shopper expects. */
  catalogue: 30_000,
  /** A cart: the basket total is the number a customer is about to be charged. */
  cart: 5_000,
  /** An order or a payment: correctness matters more than a saved request. */
  money: 10_000,
  /** A dashboard, which is read often and changes slowly. */
  analytics: 60_000,
  /** The signed-in identity. */
  session: 5 * 60_000,
} as const;

export const PAGE_SIZE = {
  default: 20,
  max: 100,
} as const;

export const SORT_OPTIONS = [
  { value: "Newest", label: "Newest first" },
  { value: "PriceAsc", label: "Price: low to high" },
  { value: "PriceDesc", label: "Price: high to low" },
  { value: "Popular", label: "Best selling" },
  { value: "Rating", label: "Top rated" },
  { value: "NameAsc", label: "Name: A to Z" },
] as const;

/**
 * Badge tone per domain.
 *
 * Order, payment and refund statuses are kept apart on purpose: `Processing` means one thing
 * to a customer watching an order and another to an admin working a refund queue, and a shared
 * map would quietly conflate them.
 */
export type Tone = "success" | "warning" | "danger" | "info" | "neutral" | "violet";

export const ORDER_STATUS_TONE: Record<string, Tone> = {
  Pending: "warning",
  Confirmed: "info",
  Processing: "info",
  Packed: "violet",
  Shipped: "violet",
  Delivered: "success",
  Completed: "success",
  Cancelled: "danger",
  Returned: "neutral",
};

export const PAYMENT_STATUS_TONE: Record<string, Tone> = {
  Initiated: "warning",
  Pending: "warning",
  Processing: "info",
  Succeeded: "success",
  Failed: "danger",
  Cancelled: "danger",
  Refunded: "neutral",
  PartiallyRefunded: "warning",
};

export const REFUND_STATUS_TONE: Record<string, Tone> = {
  Requested: "warning",
  UnderReview: "warning",
  Approved: "info",
  Rejected: "danger",
  Processing: "info",
  Completed: "success",
  Failed: "danger",
  Cancelled: "neutral",
};

export const ROLES = {
  customer: "Customer",
  seller: "Seller",
  admin: "Admin",
  superAdmin: "SuperAdmin",
} as const;
