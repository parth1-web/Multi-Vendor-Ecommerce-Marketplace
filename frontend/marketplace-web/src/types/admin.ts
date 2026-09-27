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
  conversionRate: number;
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

export interface SalesReportRow {
  period: string;
  orders: number;
  grossRevenue: number;
  discounts: number;
  commission: number;
  netToSellers: number;
  tax: number;
  shipping: number;
  refunds: number;
  netRevenue: number;
}

export interface SellerReportRow {
  sellerId: string;
  storeName: string;
  status: string;
  products: number;
  orders: number;
  grossRevenue: number;
  commission: number;
  netEarnings: number;
  averageRating: number;
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
  payouts: number;
  paidOut: number;
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

export type AdminRange = DateRange;

export type { CategorySales, OrderStatusCount, ProductStatus, RevenuePoint, TopProduct };
