"use client";

/**
 * Catalogue queries.
 *
 * The listing keeps the previous page on screen while the next one loads, because a filter
 * change that blanks the grid reads as "no results" when it is really "loading".
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { STALE_TIME } from "@/lib/constants";
import { queryKeys } from "@/lib/queryKeys";

import { categoryApi, productApi } from "./productApi";
import type { Category, ProductPage, ProductQuery } from "@/types/product";

export function useProducts(params: ProductQuery) {
  return useQuery<ProductPage>({
    queryKey: queryKeys.products.list(params),
    queryFn: ({ signal }) => productApi.list(params, signal),
    placeholderData: keepPreviousData,
    staleTime: STALE_TIME.catalogue,
  });
}

export function useCategories() {
  return useQuery<Category[]>({
    queryKey: queryKeys.categories.tree(),
    queryFn: ({ signal }) => categoryApi.tree(signal),
    staleTime: STALE_TIME.catalogue * 10,
  });
}
