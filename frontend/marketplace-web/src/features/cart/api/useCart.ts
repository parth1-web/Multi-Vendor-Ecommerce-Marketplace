"use client";

/** Cart queries and the one mutation the catalogue needs. */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { STALE_TIME } from "@/lib/constants";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { ApiErrorShape } from "@/types/api";

import { cartApi } from "./cartApi";
import type { CartResponse } from "@/types/cart";

export function useCart() {
  return useQuery<CartResponse>({
    queryKey: queryKeys.cart.detail(),
    queryFn: ({ signal }) => cartApi.get(signal),
    // The basket total is the number a customer is about to be charged, so it is the one
    // thing in the app that is never allowed to be stale.
    staleTime: STALE_TIME.cart,
  });
}

export function useUpdateCartItem() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ itemId, quantity }: { itemId: string; quantity: number }) => cartApi.updateItem(itemId, quantity),
    // The API answers with the recalculated basket, so nothing needs refetching.
    onSuccess: (cart) => queryClient.setQueryData(queryKeys.cart.detail(), cart),
  });
}

export function useRemoveCartItem() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (itemId: string) => cartApi.removeItem(itemId),
    onSuccess: (cart) => queryClient.setQueryData(queryKeys.cart.detail(), cart),
  });
}

export function useAddToCart() {
  const queryClient = useQueryClient();
  const { push } = useToast();

  return useMutation({
    mutationFn: ({ productId, productVariantId, quantity }: { productId: string; productVariantId: string; quantity?: number }) =>
      cartApi.addItem(productId, productVariantId, quantity ?? 1),

    onSuccess: (cart) => {
      // The API returns the whole basket, so the cache is replaced rather than refetched.
      queryClient.setQueryData(queryKeys.cart.detail(), cart);
      push({ tone: "success", title: "Added to your basket" });
    },

    // The response interceptor normalises every failure into an ApiErrorShape, so the reason is
    // on `detail`. Reading the raw axios envelope here found nothing and showed "please try
    // again" for every cause, which is the one message that cannot help anybody.
    onError: (error: ApiErrorShape) => {
      push({
        tone: "danger",
        title: "Could not add to basket",
        body: error.detail ?? "Please try again.",
      });
    },
  });
}
