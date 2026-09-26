/** Product types, mirroring the API's DTOs. */

import type { PagedResult } from "@/types/api";

export type ProductStatus = "Draft" | "PendingApproval" | "Published" | "Rejected" | "Archived";

export interface ProductSummary {
  id: string;
  name: string;
  slug: string;
  shortDescription: string;
  basePrice: number;
  compareAtPrice: number | null;
  discountPercentage: number;
  primaryImageUrl: string | null;
  primaryImageAlt: string | null;
  sellerId: string;
  sellerName: string;
  storeName: string;
  storeSlug: string;
  categoryId: string;
  categoryName: string;
  categorySlug: string;
  ratingAverage: number;
  ratingCount: number;
  isInStock: boolean;
  availableQuantity: number;
  isFeatured: boolean;
  isNew: boolean;
  soldCount: number;
  createdAt: string;
}

export interface ProductVariant {
  id: string;
  sku: string;
  name: string;
  price: number | null;
  isActive: boolean;
  availableQuantity: number;
  reservedQuantity: number;
  lowStockThreshold: number;
  isLowStock: boolean;
  isOutOfStock: boolean;
}

export interface ProductImage {
  id: string;
  url: string;
  altText: string | null;
  isPrimary: boolean;
  sortOrder: number;
}

export interface ProductSpecification {
  key: string;
  value: string;
  sortOrder: number;
}

export interface ReviewSummary {
  id: string;
  authorName: string;
  rating: number;
  title: string | null;
  body: string;
  createdAt: string;
  isVerifiedPurchase: boolean;
}

export interface ProductDetail extends ProductSummary {
  description: string;
  brand: string | null;
  model: string | null;
  images: ProductImage[];
  variants: ProductVariant[];
  specifications: ProductSpecification[];
  reviews: ReviewSummary[];
  tags: string[];
}

export interface Category {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  parentId: string | null;
  displayOrder: number;
  iconUrl: string | null;
  isActive: boolean;
  productCount: number;
  children: Category[];
}

export interface StoreSummary {
  sellerId: string;
  storeName: string;
  storeSlug: string;
  description: string | null;
  logoUrl: string | null;
  bannerUrl: string | null;
  ratingAverage: number;
  ratingCount: number;
  productCount: number;
  joinedAt: string;
  isVerified: boolean;
}

export type ProductSort = "Newest" | "PriceAsc" | "PriceDesc" | "Rating" | "Popular" | "NameAsc" | "NameDesc" | "Discount";

export interface ProductQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  categoryId?: string;
  categorySlug?: string;
  sellerId?: string;
  sellerSlug?: string;
  minPrice?: number;
  maxPrice?: number;
  minRating?: number;
  inStock?: boolean;
  onSale?: boolean;
  status?: ProductStatus;
  sort?: ProductSort;
  includeSubcategories?: boolean;
}

export type ProductPage = PagedResult<ProductSummary>;
