/** Seller-authored product types, mirroring the API's own listing and detail shapes. */

import type { ProductImage, ProductSpecification, ProductStatus, ProductVariant } from "@/types/product";

/**
 * Why a moderator sent a listing back.
 *
 * These are the API's own names, because the seller is shown this value and an audit of it has to
 * mean the same thing to both sides. The wording a reviewer picks from lives with the queue.
 */
export type ProductRejectionReason =
  | "None"
  | "InaccurateDescription"
  | "ProhibitedItem"
  | "CopyrightConcern"
  | "PricingIssue"
  | "MissingDocumentation"
  | "Other";

/**
 * A seller's own view of a listing, in full.
 *
 * The public detail is a shopper's view: it says nothing about moderation, and a seller cannot
 * fix what they are not shown. This is the shape that carries the reason a moderator sent a
 * listing back.
 */
export interface SellerProductDetail {
  id: string;
  name: string;
  slug: string;
  shortDescription: string;
  description: string;
  basePrice: number;
  compareAtPrice: number | null;
  discountPercentage: number;
  categoryId: string;
  categoryName: string;
  brand: string | null;
  model: string | null;
  status: ProductStatus;
  rejectionReason: string | null;
  rejectionNote: string | null;
  isFeatured: boolean;
  isInStock: boolean;
  availableQuantity: number;
  soldCount: number;
  viewCount: number;
  ratingAverage: number;
  ratingCount: number;
  createdAt: string;
  publishedAt: string | null;
  images: ProductImage[];
  variants: ProductVariant[];
  specifications: ProductSpecification[];
  tags: string[];
}

export interface CreateVariantInput {
  sku: string;
  name: string;
  price?: number | null;
  initialStock: number;
  lowStockThreshold?: number | null;
  options: { name: string; value: string }[];
}

export interface CreateImageInput {
  url: string;
  altText?: string | null;
  isPrimary: boolean;
}

export interface CreateProductRequest {
  name: string;
  slug?: string | null;
  shortDescription: string;
  description: string;
  categoryId: string;
  basePrice: number;
  compareAtPrice?: number | null;
  brand?: string | null;
  model?: string | null;
  images: CreateImageInput[];
  variants: CreateVariantInput[];
  specifications: { key: string; value: string }[];
  tags: string[];
}

export interface UpdateProductRequest {
  name: string;
  slug?: string | null;
  shortDescription: string;
  description: string;
  categoryId: string;
  basePrice: number;
  compareAtPrice?: number | null;
  brand?: string | null;
  model?: string | null;
  tags?: string[];
}
