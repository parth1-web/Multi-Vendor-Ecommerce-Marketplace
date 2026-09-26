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
  price: number;
  isActive: boolean;
  availableQuantity: number;
  isInStock: boolean;
  lowStockThreshold: number;
  options: ProductVariantOption[];
}

export interface ProductVariantOption {
  name: string;
  value: string;
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

export interface ReviewReply {
  id: string;
  body: string;
  authorName: string;
  createdAt: string;
}

export interface ReviewSummary {
  id: string;
  rating: number;
  title: string;
  body: string;
  authorName: string;
  isVerifiedPurchase: boolean;
  helpfulCount: number;
  createdAt: string;
  reply: ReviewReply | null;
}

export interface RatingBreakdown {
  average: number;
  total: number;
  fiveStar: number;
  fourStar: number;
  threeStar: number;
  twoStar: number;
  oneStar: number;
}

/** The trimmed shape the API returns for anything that only needs to be shown, not clicked. */
export interface RelatedProduct {
  id: string;
  name: string;
  slug: string;
  basePrice: number;
  compareAtPrice: number | null;
  discountPercentage: number;
  primaryImageUrl: string | null;
  ratingAverage: number;
  ratingCount: number;
  storeName: string;
  isInStock: boolean;
}

export interface ProductDetail extends Omit<ProductSummary, "primaryImageUrl" | "primaryImageAlt" | "isNew" | "availableQuantity" | "categoryId"> {
  description: string;
  brand: string | null;
  model: string | null;
  status: ProductStatus;
  storeLogoUrl: string | null;
  storeRating: number;
  storeRatingCount: number;
  reviewCount: number;
  viewCount: number;
  availableQuantity: number;
  images: ProductImage[];
  variants: ProductVariant[];
  specifications: ProductSpecification[];
  tags: string[];
  relatedProducts: RelatedProduct[];
  reviews: ReviewSummary[];
  ratingBreakdown: RatingBreakdown;
}

export interface BreadcrumbItem {
  name: string;
  slug: string;
  url: string;
}

export interface Category {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
  description: string | null;
  imageUrl: string | null;
  displayOrder: number;
  isActive: boolean;
  productCount: number;
  children: Category[];
  breadcrumb: BreadcrumbItem[];
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
