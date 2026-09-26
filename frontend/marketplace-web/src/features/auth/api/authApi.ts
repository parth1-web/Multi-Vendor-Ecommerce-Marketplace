/** Authentication endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type { ChangePasswordRequest, LoginRequest, RegisterRequest, TokenResponse, UserResponse } from "@/types/auth";

export const authApi = {
  async login(request: LoginRequest): Promise<TokenResponse> {
    const { data } = await apiClient.post<TokenResponse>("/api/auth/login", request);
    return data;
  },

  async register(request: RegisterRequest): Promise<TokenResponse> {
    const { data } = await apiClient.post<TokenResponse>("/api/auth/register", request);
    return data;
  },

  async logout(): Promise<void> {
    // The refresh token is an HttpOnly cookie, so this is a cookie-only call.
    await apiClient.post("/api/auth/logout", {});
  },

  async me(): Promise<UserResponse> {
    const { data } = await apiClient.get<UserResponse>("/api/auth/me");
    return data;
  },

  async updateProfile(request: Partial<UserResponse>): Promise<UserResponse> {
    const { data } = await apiClient.patch<UserResponse>("/api/auth/me", request);
    return data;
  },

  async changePassword(request: ChangePasswordRequest): Promise<void> {
    await apiClient.post("/api/auth/change-password", request);
  },
};
