/** Checkout, order and address endpoints. The only module that touches these paths. */

import { apiClient } from "@/api/axiosClient";
import type {
  Address,
  CheckoutRequest,
  CheckoutResponse,
  CreateAddressRequest,
  Order,
  OrderPage,
  Payment,
  Quote,
  QuoteRequest,
  UpdateAddressRequest,
} from "@/types/order";

export const addressApi = {
  async list(): Promise<Address[]> {
    const { data } = await apiClient.get<Address[]>("/api/addresses");
    return data;
  },

  async create(request: CreateAddressRequest): Promise<Address> {
    const { data } = await apiClient.post<Address>("/api/addresses", request);
    return data;
  },

  async update(id: string, request: UpdateAddressRequest): Promise<Address> {
    const { data } = await apiClient.put<Address>(`/api/addresses/${id}`, request);
    return data;
  },

  async remove(id: string): Promise<void> {
    await apiClient.delete(`/api/addresses/${id}`);
  },
};

export const checkoutApi = {
  /**
   * Prices the basket without creating an order.
   *
   * A quote rather than a total read off the cart, because the cart's own total is an estimate:
   * shipping depends on the address and the discount on the coupon, and both can change between
   * opening the page and pressing the button.
   */
  async quote(request: QuoteRequest): Promise<Quote> {
    const { data } = await apiClient.post<Quote>("/api/checkout/quote", request);
    return data;
  },

  async checkout(request: CheckoutRequest): Promise<CheckoutResponse> {
    const { data } = await apiClient.post<CheckoutResponse>("/api/checkout", request);
    return data;
  },
};

export const paymentApi = {
  /** The payments this customer has made, newest first. Scoped by the token, not by a parameter. */
  async mine(): Promise<Payment[]> {
    const { data } = await apiClient.get<Payment[]>("/api/payments/mine");
    return data;
  },

  /**
   * Asks the server to confirm a payment with the gateway.
   *
   * The customer never says a payment succeeded; they say they have been to the payment page, and
   * the server goes and finds out. This is what the pay screen calls, and it is idempotent, so
   * pressing the button twice settles one payment.
   */
  async verify(id: string): Promise<Payment> {
    const { data } = await apiClient.post<Payment>(`/api/payments/${id}/verify`, {});
    return data;
  },
};

export const orderApi = {
  async list(page = 1, pageSize = 20): Promise<OrderPage> {
    const { data } = await apiClient.get<OrderPage>(`/api/orders?page=${page}&pageSize=${pageSize}`);
    return data;
  },

  async byId(id: string): Promise<Order> {
    const { data } = await apiClient.get<Order>(`/api/orders/${id}`);
    return data;
  },

  async cancel(id: string, reason?: string): Promise<Order> {
    const { data } = await apiClient.post<Order>(`/api/orders/${id}/cancel`, { reason: reason ?? null });
    return data;
  },
};
