/** Wishlist and notification types, mirroring the API's DTOs. */

import type { PagedResult } from "@/types/api";

export interface WishlistItem {
  productId: string;
  name: string;
  slug: string;
  price: number;
  compareAtPrice: number | null;
  discountPercentage: number;
  imageUrl: string | null;
  ratingAverage: number;
  ratingCount: number;
  isInStock: boolean;
  storeName: string;
  storeSlug: string;
  addedAt: string;
}

export type NotificationType =
  | "OrderCreated"
  | "OrderConfirmed"
  | "OrderShipped"
  | "OrderDelivered"
  | "OrderCancelled"
  | "PaymentSuccessful"
  | "PaymentFailed"
  | "RefundRequested"
  | "RefundApproved"
  | "RefundRejected"
  | "SellerApproved"
  | "SellerRejected"
  | "SellerSuspended"
  | "ProductApproved"
  | "ProductRejected"
  | "LowStock"
  | "NewReview"
  | "SystemNotification"
  | "PayoutProcessed"
  | "SystemAlert";

export interface Notification {
  id: string;
  type: NotificationType;
  title: string;
  body: string;
  link: string | null;
  audience: string;
  isRead: boolean;
  createdAt: string;
}

export interface UnreadCount {
  unreadCount: number;
  totalCount: number;
}

export type NotificationPage = PagedResult<Notification>;
