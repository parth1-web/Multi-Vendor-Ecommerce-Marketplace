/** Catalogue endpoints for the browser, including the guest basket. */

import { apiClient } from "@/api/axiosClient";
import type { CartItem, CartResponse } from "@/types/cart";

export const cartApi = {
  async get(signal?: AbortSignal): Promise<CartResponse> {
    const { data } = await apiClient.get<CartResponse>("/api/cart", { signal });
    return data;
  },

  async addItem(productId: string, productVariantId: string, quantity = 1): Promise<CartResponse> {
    const { data } = await apiClient.post<CartResponse>("/api/cart/items", { productId, productVariantId, quantity });
    return data;
  },

  async updateItem(itemId: string, quantity: number): Promise<CartResponse> {
    const { data } = await apiClient.put<CartResponse>(`/api/cart/items/${itemId}`, { quantity });
    return data;
  },

  async removeItem(itemId: string): Promise<CartResponse> {
    const { data } = await apiClient.delete<CartResponse>(`/api/cart/items/${itemId}`);
    return data;
  },

  async clear(): Promise<CartResponse> {
    const { data } = await apiClient.delete<CartResponse>("/api/cart");
    return data;
  },
};

export type { CartItem, CartResponse };
