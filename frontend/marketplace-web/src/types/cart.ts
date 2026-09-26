/** Cart types, mirroring the API's cart DTOs. */

export interface CartItem {
  id: string;
  productId: string;
  productVariantId: string;
  productName: string;
  productSlug: string;
  productImageUrl: string | null;
  variantName: string;
  sku: string;
  quantity: number;
  availableQuantity: number;
  unitPrice: number;
  lineTotal: number;
  savedForLater: boolean;
  /** True when the catalogue price moved since the line was added. */
  priceChanged: boolean;
  isInStock: boolean;
  priceChangeNote: string | null;
}

export interface CartSellerGroup {
  sellerId: string;
  sellerName: string;
  storeName: string;
  storeSlug: string;
  storeLogoUrl: string | null;
  subtotal: number;
  items: CartItem[];
}

export interface CartResponse {
  cartId: string;
  itemCount: number;
  totalQuantity: number;
  subtotal: number;
  estimatedShipping: number;
  estimatedTotal: number;
  currency: string;
  groups: CartSellerGroup[];
  lastActivityAt: string;
}
