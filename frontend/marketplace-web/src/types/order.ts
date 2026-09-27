/** Order, address and checkout types, mirroring the API's DTOs. */

import type { PagedResult } from "@/types/api";

export type OrderStatus = "Pending" | "Confirmed" | "Processing" | "Packed" | "Shipped" | "Delivered" | "Cancelled" | "Returned" | "Completed";

export interface Address {
  id: string;
  label: string;
  recipientName: string;
  phoneNumber: string;
  line1: string;
  line2: string | null;
  city: string;
  state: string | null;
  postalCode: string;
  country: string;
  isDefault: boolean;
  createdAt: string;
}

export interface CreateAddressRequest {
  label: string;
  recipientName: string;
  phoneNumber: string;
  line1: string;
  line2?: string | null;
  city: string;
  state?: string | null;
  postalCode: string;
  country: string;
  isDefault?: boolean;
}

export type UpdateAddressRequest = CreateAddressRequest;

export interface QuoteSellerLine {
  sellerId: string;
  storeName: string;
  subtotal: number;
  discountAmount: number;
  shippingAmount: number;
  taxAmount: number;
  totalAmount: number;
}

export interface CouponSummary {
  code: string;
  isValid: boolean;
  message: string | null;
  discountAmount: number;
}

export interface Quote {
  subtotal: number;
  discountAmount: number;
  shippingAmount: number;
  taxAmount: number;
  totalAmount: number;
  currency: string;
  itemCount: number;
  sellerBreakdown: QuoteSellerLine[];
  coupon: CouponSummary | null;
  message: string | null;
}

export interface QuoteRequest {
  shippingAddressId?: string | null;
  couponCode?: string | null;
  shippingMethod?: string | null;
  useDefaultAddress?: boolean;
}

export interface CheckoutRequest {
  shippingAddressId: string;
  paymentMethod: string;
  couponCode?: string | null;
  customerNote?: string | null;
  shippingMethod?: string | null;
  /**
   * Sent so a double click, a refresh, or a flaky connection cannot place the same order twice.
   * The server treats a repeated key as the same order rather than a new one.
   */
  idempotencyKey: string;
}

export interface CheckoutResponse {
  orderId: string;
  orderNumber: string;
  totalAmount: number;
  currency: string;
  status: OrderStatus;
  sellerCount: number;
  paymentMethod: string;
  paymentRedirectUrl: string | null;
  paymentRequiresAction: boolean;
  message: string | null;
}

export interface OrderItem {
  id: string;
  productId: string;
  productVariantId: string;
  sellerId: string;
  storeName: string;
  productName: string;
  productImageUrl: string | null;
  variantName: string;
  sku: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  /** Whether this person has already reviewed the item, and whether they still may. */
  isReviewed: boolean;
  canReview: boolean;
  canRefund: boolean;
}

/** One store's part of an order. A marketplace order is a parent with a child per seller. */
export interface SellerOrderSummary {
  id: string;
  sellerOrderNumber: string;
  sellerId: string;
  orderId: string;
  orderNumber: string;
  storeName: string;
  status: string;
  subtotal: number;
  discountAmount: number;
  shippingAmount: number;
  totalAmount: number;
  commissionRate: number;
  commissionAmount: number;
  sellerEarnings: number;
  carrierName: string | null;
  trackingNumber: string | null;
  estimatedDeliveryAt: string | null;
}

export interface AddressSnapshot {
  recipientName: string;
  phoneNumber: string;
  line1: string;
  line2: string | null;
  city: string;
  state: string | null;
  postalCode: string;
  country: string;
}

/**
 * One step of the progress timeline.
 *
 * The API sends the step as a name and a label of its own, plus whether it has been reached and
 * whether it is where the order is now. Progress is therefore not worked out by comparing dates
 * here: an order that is awaiting collection has reached a step that has no timestamp, and a
 * client that infers progress from dates renders it as "waiting" while it is in fact current.
 */
export interface TimelineStep {
  step: string;
  label: string;
  at: string | null;
  isComplete: boolean;
  isCurrent: boolean;
  note: string | null;
}

export interface Order {
  id: string;
  orderNumber: string;
  status: OrderStatus;
  placedAt: string;
  subtotal: number;
  discountAmount: number;
  shippingAmount: number;
  taxAmount: number;
  totalAmount: number;
  refundedAmount: number;
  currency: string;
  isPaid: boolean;
  paymentMethod: string;
  paymentStatus: string;
  couponCode: string | null;
  shippingAddress: AddressSnapshot;
  customerNote: string | null;
  cancellationReason: string | null;
  sellerCount: number;
  items: OrderItem[];
  sellerOrders: SellerOrderSummary[];
  timeline: TimelineStep[];
}

export interface OrderListItem {
  id: string;
  orderNumber: string;
  status: OrderStatus;
  placedAt: string;
  totalAmount: number;
  currency: string;
  isPaid: boolean;
  firstProductName: string | null;
  firstProductImageUrl: string | null;
  itemCount: number;
  sellerCount: number;
  sellerNames: string | null;
}

export type OrderPage = PagedResult<OrderListItem>;

/** One of the payments a customer has made, as the payments page and the pay screen read it. */
export type PaymentStatus = "Pending" | "Processing" | "Succeeded" | "Failed" | "Refunded" | "PartiallyRefunded";

export interface Payment {
  id: string;
  orderId: string;
  orderNumber: string;
  provider: string;
  status: PaymentStatus;
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
}
