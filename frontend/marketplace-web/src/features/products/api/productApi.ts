/** Catalogue reads. The only module that touches the public product paths. */

import { apiClient } from "@/api/axiosClient";
import { PAGE_SIZE } from "@/lib/constants";
import type { Category, ProductDetail, ProductPage, ProductQuery, ProductSummary } from "@/types/product";
import type { StoreProfile } from "@/types/store";

/**
 * Turns the query object into a query string, dropping anything unset.
 *
 * Sending `undefined` as a literal would make the API filter on the string "undefined", so
 * every caller would have to remember to strip its own empties.
 */
function toSearchParams(params: Record<string, unknown>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === "") {
      continue;
    }

    if (typeof value === "boolean") {
      // Only a deliberate true is worth sending; a false is the absence of a filter.
      if (value) {
        search.set(key, "true");
      }
      continue;
    }

    search.set(key, String(value));
  }

  return search.toString();
}

export const productApi = {
  async list(params: ProductQuery, signal?: AbortSignal): Promise<ProductPage> {
    const { data } = await apiClient.get<ProductPage>(`/api/products?${toSearchParams({ ...params, pageSize: params.pageSize ?? PAGE_SIZE.default })}`, {
      signal,
    });

    return data;
  },

  async bySlug(slug: string, signal?: AbortSignal): Promise<ProductDetail> {
    const { data } = await apiClient.get<ProductDetail>(`/api/products/slug/${encodeURIComponent(slug)}`, { signal });
    return data;
  },

  async featured(take = 8, signal?: AbortSignal): Promise<ProductSummary[]> {
    const { data } = await apiClient.get<ProductSummary[]>(`/api/products/featured?take=${take}`, { signal });
    return data;
  },

  async bestSellers(take = 8, signal?: AbortSignal): Promise<ProductSummary[]> {
    const { data } = await apiClient.get<ProductSummary[]>(`/api/products/best-sellers?take=${take}`, { signal });
    return data;
  },

  async newArrivals(take = 8, signal?: AbortSignal): Promise<ProductSummary[]> {
    const { data } = await apiClient.get<ProductSummary[]>(`/api/products/new-arrivals?take=${take}`, { signal });
    return data;
  },
};

export const categoryApi = {
  async tree(signal?: AbortSignal): Promise<Category[]> {
    const { data } = await apiClient.get<Category[]>("/api/categories", { signal });
    return data;
  },

  async bySlug(slug: string, signal?: AbortSignal): Promise<Category> {
    const { data } = await apiClient.get<Category>(`/api/categories/${encodeURIComponent(slug)}`, { signal });
    return data;
  },
};
export const storeApi = {
  /** The storefront, with one page of its products already attached. */
  async bySlug(slug: string, signal?: AbortSignal): Promise<StoreProfile> {
    const { data } = await apiClient.get<StoreProfile>(`/api/stores/${encodeURIComponent(slug)}`, { signal });

    return data;
  },
};
