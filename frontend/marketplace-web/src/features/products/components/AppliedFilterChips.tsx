"use client";

/**
 * The applied-filter overview, read from the URL rather than component state.
 *
 * Every chip is a link that removes exactly one filter and resets the page, so a filter never
 * has to be reopened to be undone. Sort is deliberately not a chip: sorting reorders relevant
 * products while filtering removes irrelevant ones, and conflating them teaches the wrong
 * mental model. Page-owned context (the category of a category page, the store of a store page)
 * is shown by the page header instead, never offered for removal.
 */

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { X } from "lucide-react";

import { listingHref } from "@/features/products/api/readProductQuery";
import { storeApi } from "@/features/products/api/productApi";
import { useCategories } from "@/features/products/api/useProducts";
import { flattenCategories } from "@/lib/categories";
import { STALE_TIME } from "@/lib/constants";
import { formatCurrency } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { ProductQuery } from "@/types/product";

interface AppliedFilterChipsProps {
  query: ProductQuery;
  basePath: string;
  /** The address-bar name of the search term: `q` on /search, `search` everywhere else. */
  searchParamName?: "search" | "q";
  /** Slugs owned by the page itself; shown as context elsewhere, never as removable chips. */
  lockedCategorySlug?: string;
  lockedSellerSlug?: string;
}

interface Chip {
  key: string;
  label: string;
  removeHref: string;
}

/** How many removable filters the query carries, for the mobile filter button badge. */
export function countActiveFilters(
  query: ProductQuery,
  locks: { lockedCategorySlug?: string; lockedSellerSlug?: string } = {},
): number {
  let count = 0;

  if (query.search) {
    count += 1;
  }

  if (query.categorySlug && query.categorySlug !== locks.lockedCategorySlug) {
    count += 1;
  }

  if (query.sellerSlug && query.sellerSlug !== locks.lockedSellerSlug) {
    count += 1;
  }

  if (query.minPrice !== undefined || query.maxPrice !== undefined) {
    count += 1;
  }

  if (query.minRating !== undefined) {
    count += 1;
  }

  if (query.inStock) {
    count += 1;
  }

  if (query.onSale) {
    count += 1;
  }

  return count;
}

export function AppliedFilterChips({
  query,
  basePath,
  searchParamName = "search",
  lockedCategorySlug,
  lockedSellerSlug,
}: AppliedFilterChipsProps) {
  const { data: categories } = useCategories();
  const categoryName = query.categorySlug
    ? flattenCategories(categories ?? []).find((category) => category.slug === query.categorySlug)?.name
    : undefined;

  const sellerSlug = !lockedSellerSlug ? query.sellerSlug : undefined;
  const { data: store } = useQuery({
    queryKey: queryKeys.stores.detail(sellerSlug ?? ""),
    queryFn: () => storeApi.bySlug(sellerSlug ?? ""),
    enabled: Boolean(sellerSlug),
    staleTime: STALE_TIME.catalogue,
  });

  const chips = buildChips(query, {
    basePath,
    searchParamName,
    lockedCategorySlug,
    lockedSellerSlug,
    categoryName,
    storeName: store?.name,
  });

  if (chips.length === 0) {
    return null;
  }

  return (
    <div className="mp-filter-chips" role="group" aria-label="Applied filters">
      {chips.map((chip) => (
        <Link key={chip.key} className="mp-chip" href={chip.removeHref} aria-label={`Remove filter: ${chip.label}`}>
          <span>{chip.label}</span>
          <X size={12} aria-hidden />
        </Link>
      ))}
      <Link
        className="mp-chip mp-chip-clear"
        href={listingHref(basePath, clearQuery(query, { searchParamName, lockedCategorySlug, lockedSellerSlug }), 1)}
        aria-label="Clear all filters"
      >
        Clear all
      </Link>
    </div>
  );
}

interface ChipContext {
  basePath: string;
  searchParamName: "search" | "q";
  lockedCategorySlug?: string;
  lockedSellerSlug?: string;
  categoryName?: string;
  storeName?: string;
}

function buildChips(query: ProductQuery, context: ChipContext): Chip[] {
  const chips: Chip[] = [];

  if (query.search) {
    chips.push({
      key: "search",
      label: `“${query.search}”`,
      removeHref: listingHref(context.basePath, toUrlQuery(query, context, { search: undefined }), 1),
    });
  }

  if (query.categorySlug && query.categorySlug !== context.lockedCategorySlug) {
    const name = context.categoryName ?? humanize(query.categorySlug);
    chips.push({
      key: "category",
      label: name,
      removeHref: listingHref(context.basePath, toUrlQuery(query, context, { categorySlug: undefined }), 1),
    });
  }

  if (query.sellerSlug && query.sellerSlug !== context.lockedSellerSlug) {
    const name = context.storeName ?? humanize(query.sellerSlug);
    chips.push({
      key: "seller",
      label: name,
      removeHref: listingHref(context.basePath, toUrlQuery(query, context, { sellerSlug: undefined }), 1),
    });
  }

  if (query.minPrice !== undefined || query.maxPrice !== undefined) {
    chips.push({
      key: "price",
      label: priceLabel(query.minPrice, query.maxPrice),
      removeHref: listingHref(
        context.basePath,
        toUrlQuery(query, context, { minPrice: undefined, maxPrice: undefined }),
        1,
      ),
    });
  }

  if (query.minRating !== undefined) {
    chips.push({
      key: "rating",
      label: `${query.minRating} stars & up`,
      removeHref: listingHref(context.basePath, toUrlQuery(query, context, { minRating: undefined }), 1),
    });
  }

  if (query.inStock) {
    chips.push({
      key: "inStock",
      label: "In stock",
      removeHref: listingHref(context.basePath, toUrlQuery(query, context, { inStock: undefined }), 1),
    });
  }

  if (query.onSale) {
    chips.push({
      key: "onSale",
      label: "On sale",
      removeHref: listingHref(context.basePath, toUrlQuery(query, context, { onSale: undefined }), 1),
    });
  }

  return chips;
}

/**
 * The query the address bar should hold after a change.
 *
 * Search keeps `q` in the address bar while the API expects `search`; the mapping lives here so
 * pagination, chips, and the clear action cannot disagree about it.
 */
function toUrlQuery(
  query: ProductQuery,
  context: Pick<ChipContext, "searchParamName">,
  overrides: Record<string, unknown>,
): Record<string, unknown> {
  const merged: Record<string, unknown> = { ...query, ...overrides, page: undefined };

  if (context.searchParamName === "q") {
    const { search, ...rest } = merged;
    return search ? { ...rest, q: search } : rest;
  }

  return merged;
}

/**
 * Clear-all removes filters and resets the page, but keeps the discovery context: the search
 * term, the sort order, and anything the page itself owns. Clearing the search box by accident
 * is exactly the kind of data loss this avoids.
 */
function clearQuery(
  query: ProductQuery,
  context: Pick<ChipContext, "searchParamName" | "lockedCategorySlug" | "lockedSellerSlug">,
): Record<string, unknown> {
  return toUrlQuery(
    query,
    context,
    {
      categorySlug: context.lockedCategorySlug ? query.categorySlug : undefined,
      sellerSlug: context.lockedSellerSlug ? query.sellerSlug : undefined,
      minPrice: undefined,
      maxPrice: undefined,
      minRating: undefined,
      inStock: undefined,
      onSale: undefined,
    },
  );
}

function priceLabel(min?: number, max?: number): string {
  if (min !== undefined && max !== undefined) {
    return `${formatCurrency(min)} – ${formatCurrency(max)}`;
  }

  if (min !== undefined) {
    return `From ${formatCurrency(min)}`;
  }

  return `Up to ${formatCurrency(max ?? 0)}`;
}

/** A slug is a machine name; a chip is read by a person. */
function humanize(slug: string): string {
  return slug
    .split("-")
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}
