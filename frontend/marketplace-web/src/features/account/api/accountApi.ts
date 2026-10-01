/** Wishlist and notification endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type { NotificationPage, UnreadCount, WishlistItem } from "@/types/account";

export const wishlistApi = {
  async list(): Promise<WishlistItem[]> {
    const { data } = await apiClient.get<WishlistItem[]>("/api/wishlist");
    return data;
  },

  async add(productId: string): Promise<WishlistItem[]> {
    const { data } = await apiClient.post<WishlistItem[]>("/api/wishlist", { productId });
    return data;
  },

  async remove(productId: string): Promise<void> {
    await apiClient.delete(`/api/wishlist/${productId}`);
  },

  async clear(): Promise<void> {
    await apiClient.delete("/api/wishlist");
  },
};

export const notificationApi = {
  async list(page = 1, pageSize = 20, unreadOnly = false, type?: string): Promise<NotificationPage> {
    const { data } = await apiClient.get<NotificationPage>(
      `/api/notifications?page=${page}&pageSize=${pageSize}${unreadOnly ? "&unreadOnly=true" : ""}${type ? `&type=${encodeURIComponent(type)}` : ""}`,
    );
    return data;
  },

  async unreadCount(): Promise<UnreadCount> {
    const { data } = await apiClient.get<UnreadCount>("/api/notifications/unread-count");
    return data;
  },

  async markRead(id: string): Promise<void> {
    await apiClient.put(`/api/notifications/${id}/read`, {});
  },

  async markAllRead(): Promise<void> {
    await apiClient.put("/api/notifications/read-all", {});
  },

  async remove(id: string): Promise<void> {
    await apiClient.delete(`/api/notifications/${id}`);
  },
};
