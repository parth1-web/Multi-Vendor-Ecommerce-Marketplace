/**
 * Turns a listing URL into a typed query.
 *
 * A value that cannot be parsed is dropped rather than forwarded: sending `NaN` to the API
 * turns a mistyped link into a filter nobody asked for, and a filter that silently does
 * nothing is worse than one that is visibly absent.
 */

import { PAGE_SIZE } from "@/lib/constants";
import type { ProductQuery, ProductSort } from "@/types/product";

const SORTS: ProductSort[] = ["Newest", "PriceAsc", "PriceDesc", "Rating", "Popular", "NameAsc", "NameDesc", "Discount"];

export function readProductQuery(params: Record<string, string | string[] | undefined>): ProductQuery {
  const value = (key: string): string | undefined => {
    const raw = params[key];
    const single = Array.isArray(raw) ? raw[0] : raw;
    return single === "" ? undefined : single;
  };

  const number = (key: string): number | undefined => {
    const raw = value(key);
    const parsed = raw === undefined ? Number.NaN : Number(raw);

    return Number.isFinite(parsed) && parsed > 0 ? parsed : undefined;
  };

  const flag = (key: string): boolean | undefined => {
    const raw = value(key);
    return raw === undefined ? undefined : raw === "true";
  };

  const sort = value("sort") as ProductSort | undefined;

  return {
    page: number("page") ?? 1,
    pageSize: number("pageSize") ?? PAGE_SIZE.default,
    search: value("search"),
    categorySlug: value("categorySlug"),
    sellerSlug: value("sellerSlug"),
    minPrice: number("minPrice"),
    maxPrice: number("maxPrice"),
    minRating: number("minRating"),
    inStock: flag("inStock"),
    onSale: flag("onSale"),
    includeSubcategories: flag("includeSubcategories"),
    sort: sort && SORTS.includes(sort) ? sort : undefined,
  };
}

/**
 * The listing URL for a page, keeping the filters that produced it.
 *
 * Pagination has to carry the filters with it: a next link that drops them lands the shopper
 * on page two of the wrong result set, which looks like the end of the catalogue.
 */
export function listingHref(basePath: string, query: ProductQuery, page: number): string {
  const search = new URLSearchParams();

  for (const [key, raw] of Object.entries(query)) {
    if (raw === undefined || raw === false || key === "page") {
      continue;
    }

    search.set(key, String(raw));
  }

  if (page > 1) {
    search.set("page", String(page));
  }

  const queryString = search.toString();

  return queryString ? `${basePath}?${queryString}` : basePath;
}
