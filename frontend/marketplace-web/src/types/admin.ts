/** Admin console types, mirroring the moderation, user and report DTOs. */

import type { PagedResult } from "@/types/api";
import type { UserRole } from "@/types/auth";
import type { CategorySales, DateRange, OrderStatusCount, RevenuePoint, TopProduct } from "@/types/seller";
import type { ProductStatus } from "@/types/product";

export interface AdminSummary {
  totalCustomers: number;
  totalSellers: number;
  activeSellers: number;
  pendingSellers: number;
  suspendedSellers: number;
  totalProducts: number;
  publishedProducts: number;
  pendingProducts: number;
  totalOrders: number;
  pendingOrders: number;
  openRefunds: number;
  totalRevenue: number;
  commissionRevenue: number;
  sellerPayouts: number;
  refundedAmount: number;
  refundRate: number;
  averageOrderValue: number;
  newCustomersThisMonth: number;
  newSellersThisMonth: number;
  ordersToday: number;
  revenueToday: number;
  generatedAt: string;
}

export interface GrowthPoint {
  period: string;
  customers: number;
  sellers: number;
  products: number;
  orders: number;
}

export interface RefundAnalytics {
  totalRequests: number;
  approved: number;
  rejected: number;
  pending: number;
  amount: number;
  rate: number;
}

export interface AdminUser {
  id: string;
  email: string;
  fullName: string;
  role: UserRole;
  isActive: boolean;
  isEmailConfirmed: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  sellerId: string | null;
}

export type AdminUserPage = PagedResult<AdminUser>;

export interface AuditEntry {
  id: string;
  actorId: string | null;
  actorEmail: string;
  action: string;
  entityType: string;
  entityId: string | null;
  entityName: string | null;
  changesJson: string | null;
  ipAddress: string | null;
  correlationId: string;
  createdAt: string;
}

export type AuditPage = PagedResult<AuditEntry>;

/**
 * The action names the API's `AuditAction` enum defines.
 *
 * A transcription of the server's enum, not a description of what this marketplace does: the
 * filter sends one of these to `GET /api/admin/audit-logs?action=`, and the API parses it back
 * with `Enum.TryParse`, so a name that is not in the list is silently ignored and would look like
 * a filter that does nothing. Adding a name here does not create the event — only the server
 * records events — but omitting one hides events that exist.
 *
 * `CouponDeleted` is retired server-side and kept because rows written under it still exist;
 * `CouponDeactivated` replaced it. `SellerProfileUpdated` and `UserRoleChanged` are the accurate
 * names for a profile edit and a role change, added when those paths stopped reporting
 * themselves as something else.
 */
export const AUDIT_ACTIONS = [
  "Login",
  "LoginFailed",
  "Logout",
  "Register",
  "PasswordChanged",
  "RoleChanged",
  "UserStatusChanged",
  "SellerApplied",
  "SellerApproved",
  "SellerRejected",
  "SellerSuspended",
  "SellerResumed",
  "SellerProfileUpdated",
  "StoreUpdated",
  "CategoryCreated",
  "CategoryUpdated",
  "CategoryDeleted",
  "ProductCreated",
  "ProductUpdated",
  "ProductDeleted",
  "ProductApproved",
  "ProductRejected",
  "ProductFeatured",
  "InventoryAdjusted",
  "OrderCreated",
  "OrderStatusChanged",
  "OrderCancelled",
  "PaymentCreated",
  "PaymentVerified",
  "PaymentFailed",
  "WebhookReceived",
  "RefundRequested",
  "RefundApproved",
  "RefundRejected",
  "CommissionCreated",
  "CommissionReversed",
  "CommissionPaid",
  "ReviewCreated",
  "ReviewUpdated",
  "ReviewDeleted",
  "ReviewModerated",
  "CouponCreated",
  "CouponUpdated",
  "CouponDeleted",
  "CouponDeactivated",
  "InventoryThresholdChanged",
  "SettingsUpdated",
  "ReportExported",
  "PasswordResetRequested",
  "PasswordResetCompleted",
  "UserRoleChanged",
] as const;

export type AuditActionName = (typeof AUDIT_ACTIONS)[number];

/** The entity types the audit rows in this codebase carry, for the target column's wording. */
export const AUDIT_ENTITY_TYPES = [
  "User",
  "Seller",
  "SellerOrder",
  "Store",
  "Category",
  "Product",
  "Inventory",
  "Order",
  "Payment",
  "Refund",
  "Commission",
  "Payout",
  "Review",
  "Coupon",
] as const;

export type AuditEntityType = (typeof AUDIT_ENTITY_TYPES)[number];

/**
 * One period of the sales report.
 *
 * Every money column is a real aggregate of what checkout recorded on the orders in the period:
 * gross is their subtotal, discounts their coupon discount, tax and shipping what was charged,
 * refunds what has actually been returned. Cancelled orders are left out by the API; every other
 * status counts, because a placed order is revenue the marketplace has earned the right to collect.
 */
export interface SalesReportRow {
  period: string;
  orders: number;
  grossRevenue: number;
  /** Sum of the coupon discount allocated at checkout. */
  discounts: number;
  commission: number;
  netToSellers: number;
  /** Tax computed at checkout from the rate in force at the time. */
  tax: number;
  /** Delivery charged at checkout, already net of the free-shipping threshold. */
  shipping: number;
  /** Money actually returned. A request nobody has approved is not in here. */
  refunds: number;
  /**
   * Gross less discounts less refunds: the value of goods actually retained.
   *
   * Tax and shipping are excluded on purpose. Whether collected tax is platform revenue, and
   * whether shipping is income or a pass-through to a carrier, are policy questions this codebase
   * has not answered; reporting them in their own columns is honest where folding them in would be
   * a guess.
   */
  netRevenue: number;
}

/**
 * One seller in the seller report.
 *
 * `averageRating` is the mean of that seller's *visible* reviews — the ones a shopper can actually
 * see — and `null` for a seller who has none, which is not the same thing as zero. Zero is a score
 * somebody gave.
 */
export interface SellerReportRow {
  sellerId: string;
  storeName: string;
  status: string;
  products: number;
  orders: number;
  grossRevenue: number;
  commission: number;
  netEarnings: number;
  averageRating: number | null;
  /** How many visible reviews the average came from. */
  reviewCount: number;
}

export interface InventoryReportRow {
  productId: string;
  productName: string;
  sku: string;
  storeName: string;
  available: number;
  reserved: number;
  sold: number;
  threshold: number;
  stockValue: number;
}

export interface CommissionReportRow {
  sellerId: string;
  storeName: string;
  orders: number;
  grossRevenue: number;
  commissionAmount: number;
  sellerEarnings: number;
  /** Completed payouts *created* within the selected range, not lifetime totals. */
  payouts: number;
  /** As above: money paid out within the range. */
  paidOut: number;
}

/** The seller report, paged by the server. */
export type SellerReportPage = PagedResult<SellerReportRow>;

/** The inventory report, paged by the server. */
export type InventoryReportPage = PagedResult<InventoryReportRow>;

/** Filters the seller report accepts. An absent `range` means lifetime totals, not "no orders". */
export interface SellerReportQuery {
  page: number;
  pageSize?: number;
  search?: string;
  status?: string;
  range?: DateRange;
}

/** Filters the inventory report accepts. */
export interface InventoryReportQuery {
  page: number;
  pageSize?: number;
  search?: string;
  lowStockOnly?: boolean;
  outOfStockOnly?: boolean;
}

/** What a CSV export actually produced, so the browser can say so rather than guessing. */
export interface SalesExportResult {
  fileName: string;
  rowCount: number;
  /** The server's cap. A file with exactly this many rows may have been cut short. */
  rowLimit: number;
  text: string;
}

/** A listing in the moderation queue, with the notes a reviewer needs to decide. */
export interface ModerationItem {
  id: string;
  name: string;
  slug: string;
  sellerName: string;
  storeName: string;
  storeSlug: string;
  basePrice: number;
  primaryImageUrl: string | null;
  categoryName: string;
  createdAt: string;
  soldCount: number;
  isFeatured: boolean;
}

export type ModerationPage = PagedResult<ModerationItem>;

/* ---------------------------------------------------------------- payments */

/** The gateway a payment went through. The API's own names, since they are what it records. */
export type PaymentProvider = "Mock" | "CashOnDelivery" | "Khalti" | "ESewa" | "Stripe";

/** Every state a payment can be in, including two no code path currently sets. */
export type PaymentState = "Initiated" | "Pending" | "Processing" | "Succeeded" | "Failed" | "Cancelled" | "Refunded" | "PartiallyRefunded";

export interface PaymentTransaction {
  id: string;
  type: string;
  isSuccess: boolean;
  errorMessage: string | null;
  createdAt: string;
}

/**
 * A payment, as the admin list and the single-get endpoint return it.
 *
 * The two are not the same shape and the difference matters: the **list** projects `orderNumber`
 * as an empty string and returns no transaction ledger, so anything read from a row needs the
 * per-payment fetch before it can be shown. Both are typed here rather than papered over.
 */
export interface Payment {
  id: string;
  orderId: string;
  /** Empty on the list endpoint, populated by `GET /api/payments/{id}`. */
  orderNumber: string;
  provider: PaymentProvider;
  status: PaymentState;
  amount: number;
  currency: string;
  transactionReference: string;
  gatewayPaymentId: string | null;
  redirectUrl: string | null;
  qrCodeData: string | null;
  failureReason: string | null;
  requiresAction: boolean;
  initiatedAt: string;
  completedAt: string | null;
  /** Always empty on the list endpoint; populated by the single-get. */
  transactions: PaymentTransaction[];
}

export type PaymentPage = PagedResult<Payment>;

/* ---------------------------------------------------------------- refunds */

export type RefundState = "Requested" | "UnderReview" | "Approved" | "Rejected" | "Processing" | "Completed" | "Failed" | "Cancelled";

/** What the review endpoint accepts. There is no separate approve-then-complete action. */
export type RefundReviewAction = "Approve" | "Reject" | "MarkProcessing";

export interface RefundItem {
  id: string;
  orderItemId: string;
  productId: string;
  productName: string;
  sku: string;
  quantity: number;
  amount: number;
}

export interface Refund {
  id: string;
  orderId: string;
  orderNumber: string;
  paymentId: string;
  status: RefundState;
  amount: number;
  reason: string;
  description: string | null;
  reviewNote: string | null;
  rejectionReason: string | null;
  requestedAt: string;
  reviewedAt: string | null;
  completedAt: string | null;
  items: RefundItem[];
}

export type RefundPage = PagedResult<Refund>;

/* ------------------------------------------------------------- categories */

/**
 * A category as the tree endpoint returns it.
 *
 * `breadcrumb` is only filled in by the single-category endpoint; on a tree every node's is an
 * empty array, so nothing may depend on it being present.
 */
export interface AdminCategory {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
  description: string | null;
  imageUrl: string | null;
  displayOrder: number;
  isActive: boolean;
  productCount: number;
  children: AdminCategory[];
}

/** The create shape. `slug` is optional — the API generates one from the name. `isActive` is not in it. */
export interface CreateCategoryRequest {
  name: string;
  slug: string | null;
  description: string | null;
  imageUrl: string | null;
  parentId: string | null;
  displayOrder: number;
}

/**
 * The update shape.
 *
 * `isActive` is non-nullable with no default: omitting it deactivates the category silently, so
 * every call sends it explicitly. `parentId` is absent from this DTO — re-parenting is not
 * possible through the API at all.
 */
export interface UpdateCategoryRequest {
  name: string;
  slug: string | null;
  description: string | null;
  imageUrl: string | null;
  displayOrder: number;
  isActive: boolean;
}

export type AdminRange = DateRange;

export type { CategorySales, OrderStatusCount, ProductStatus, RevenuePoint, TopProduct };
