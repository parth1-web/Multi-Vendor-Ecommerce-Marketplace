/** Identity and authentication types. */

export type UserRole = "Customer" | "Seller" | "Admin" | "SuperAdmin";

export interface UserResponse {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  role: UserRole;
  isEmailConfirmed: boolean;
  isActive: boolean;
  sellerId: string | null;
  sellerStatus: string | null;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface TokenResponse {
  accessToken: string;
  expiresInSeconds: number;
  expiresAt: string;
  user: UserResponse;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  confirmPassword: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string | null;
  role?: UserRole | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}
