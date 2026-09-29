"use client";

/**
 * The filter panel. A client island inside a server-rendered page: the first page of results is
 * rendered on the server for indexing, and only the controls that change the query string are
 * interactive.
 *
 * Every change writes to the URL and resets the page. Filters that live only in component state
 * cannot be shared, bookmarked or survived by the back button, and a shopper who filters then
 * presses back expects to land where they were.
 */

import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";

import { SORT_OPTIONS } from "@/lib/constants";
import { cx, formatCurrency } from "@/lib/format";
import { flattenCategories } from "@/lib/categories";
import { useCategories } from "@/features/products/api/useProducts";

export interface ProductFiltersProps {
  totalCount: number;
  /** Where filter changes navigate: the category, search, or store page hosting the island. */
  basePath?: string;
  /** The address-bar name of the search term: `q` on /search, `search` everywhere else. */
  searchParamName?: "search" | "q";
  /** Slugs owned by the page itself; shown as context elsewhere, never cleared here. */
  lockedCategorySlug?: string;
  lockedSellerSlug?: string;
}

export function ProductFilters({
  totalCount,
  basePath = "/products",
  searchParamName = "search",
  lockedCategorySlug,
  lockedSellerSlug,
}: ProductFiltersProps) {
  const router = useRouter();
  const searchParams = useSearchParams();

  // The text box holds what is being typed; the URL holds what is being searched. A keystroke
  // per request would hammer the API and make the results flicker, and the `key` on the input
  // resets the box whenever the URL changes underneath it, so the two can never disagree.
  const [search, setSearch] = useState(searchParams.get(searchParamName) ?? "");
  const searchKey = searchParams.get(searchParamName) ?? "";

  const apply = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(searchParams.toString());

    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    // Any filter change invalidates the current page number.
    next.delete("page");
    const queryString = next.toString();
    router.push(queryString ? `${basePath}?${queryString}` : basePath);
  };

  const current = (key: string) => searchParams.get(key) ?? "";

  const clearable = hasRemovableFilters(searchParams, { searchParamName, lockedCategorySlug, lockedSellerSlug });
  const clearAll = () => {
    // Clearing removes filters, not context: the search term and sort order survive, while the
    // page-owned category or seller lives in the path and was never in danger.
    const next = new URLSearchParams();
    const term = searchParams.get(searchParamName);
    const sort = searchParams.get("sort");

    if (term) {
      next.set(searchParamName, term);
    }

    if (sort) {
      next.set("sort", sort);
    }

    const queryString = next.toString();
    router.push(queryString ? `${basePath}?${queryString}` : basePath);
  };

  // The category tree is fetched here rather than passed in, so it cannot disagree with the
  // listing about what the catalogue actually contains.
  const { data: categories } = useCategories();

  return (
    <aside aria-label="Filters" className="mp-stack">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          apply({ [searchParamName]: search.trim() || undefined });
        }}
        className="mp-stack-sm"
      >
        <label htmlFor="filter-search" className="mp-metric-label">
          Search
        </label>
        <div className="d-flex" style={{ gap: "var(--space-2)" }}>
          <input
            key={searchKey}
            id="filter-search"
            type="search"
            className="form-control form-control-sm"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Product, description, or SKU"
          />
          <button type="submit" className="btn btn-sm btn-primary">
            Go
          </button>
        </div>
      </form>

      {/* A category page owns its category through the path, so offering to change it here
          would fight the page. The category is context there, not a filter. */}
      {!lockedCategorySlug ? (
        <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
          <legend className="mp-metric-label p-0 mb-2">Category</legend>
          <select
            className="form-select form-select-sm"
            value={current("categorySlug")}
            onChange={(event) => apply({ categorySlug: event.target.value || undefined })}
            aria-label="Category"
          >
            <option value="">All categories</option>
            {flattenCategories(categories ?? []).map((category) => (
              <option key={category.id} value={category.slug}>
                {category.name}
              </option>
            ))}
          </select>
        </fieldset>
      ) : null}

      <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className="mp-metric-label p-0 mb-2">Price</legend>
        <div className="d-flex align-items-center" style={{ gap: "var(--space-2)" }}>
          <input
            type="number"
            min={0}
            step="1"
            inputMode="numeric"
            className="form-control form-control-sm"
            placeholder="Min"
            defaultValue={current("minPrice")}
            onBlur={(event) => apply({ minPrice: event.target.value || undefined })}
            aria-label="Minimum price"
          />
          <span aria-hidden style={{ color: "var(--text-subtle)" }}>
            –
          </span>
          <input
            type="number"
            min={0}
            step="1"
            inputMode="numeric"
            className="form-control form-control-sm"
            placeholder="Max"
            defaultValue={current("maxPrice")}
            onBlur={(event) => apply({ maxPrice: event.target.value || undefined })}
            aria-label="Maximum price"
          />
        </div>
      </fieldset>

      <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className="mp-metric-label p-0 mb-2">Rating</legend>
        <div className="mp-stack-sm">
          {[4, 3, 2].map((rating) => (
            <label key={rating} className="d-flex align-items-center" style={{ gap: "var(--space-2)", fontSize: "var(--fs-sm)" }}>
              <input
                type="radio"
                name="minRating"
                checked={current("minRating") === String(rating)}
                onChange={() => apply({ minRating: String(rating) })}
              />
              {rating} stars and up
            </label>
          ))}
          {current("minRating") ? (
            <button type="button" className="btn btn-sm btn-link p-0" style={{ fontSize: "var(--fs-xs)" }} onClick={() => apply({ minRating: undefined })}>
              Clear rating
            </button>
          ) : null}
        </div>
      </fieldset>

      <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className="mp-metric-label p-0 mb-2">Availability</legend>
        <div className="mp-stack-sm">
          <Check label="In stock only" checked={current("inStock") === "true"} onChange={(on) => apply({ inStock: on ? "true" : undefined })} />
          <Check label="On sale" checked={current("onSale") === "true"} onChange={(on) => apply({ onSale: on ? "true" : undefined })} />
        </div>
      </fieldset>

      <div className="mp-spread" style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-3)" }}>
        <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>
          {totalCount} {totalCount === 1 ? "product" : "products"}
        </span>
        {clearable ? (
          <button
            type="button"
            className="btn btn-sm btn-link p-0"
            style={{ fontSize: "var(--fs-xs)" }}
            onClick={clearAll}
          >
            Clear all
          </button>
        ) : null}
      </div>
    </aside>
  );
}

function Check({ label, checked, onChange }: { label: string; checked: boolean; onChange: (on: boolean) => void }) {
  return (
    <label className="d-flex align-items-center" style={{ gap: "var(--space-2)", fontSize: "var(--fs-sm)" }}>
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      {label}
    </label>
  );
}

export function SortSelect({ basePath = "/products" }: { basePath?: string }) {
  const router = useRouter();
  const searchParams = useSearchParams();

  return (
    <label className="d-flex align-items-center" style={{ gap: "var(--space-2)", fontSize: "var(--fs-sm)" }}>
      <span className="mp-metric-label">Sort</span>
      <select
        className="form-select form-select-sm"
        style={{ width: "auto" }}
        value={searchParams.get("sort") ?? "Newest"}
        aria-label="Sort products"
        onChange={(event) => {
          const next = new URLSearchParams(searchParams.toString());
          if (event.target.value === "Newest") {
            next.delete("sort");
          } else {
            next.set("sort", event.target.value);
          }
          next.delete("page");
          const queryString = next.toString();
          router.push(queryString ? `${basePath}?${queryString}` : basePath);
        }}
      >
        {SORT_OPTIONS.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

/** Anything the shopper chose that a clear action should undo, leaving page-owned context alone. */
function hasRemovableFilters(
  searchParams: Pick<URLSearchParams, "has">,
  locks: { searchParamName: "search" | "q"; lockedCategorySlug?: string; lockedSellerSlug?: string },
): boolean {
  const keys = [locks.searchParamName, "minPrice", "maxPrice", "minRating", "inStock", "onSale"];

  if (!locks.lockedCategorySlug) {
    keys.push("categorySlug");
  }

  if (!locks.lockedSellerSlug) {
    keys.push("sellerSlug");
  }

  return keys.some((key) => searchParams.has(key));
}

export function PriceRangeSummary({ min, max }: { min?: number; max?: number }) {
  if (min === undefined && max === undefined) {
    return null;
  }

  return (
    <span className={cx("mp-badge", "mp-badge-neutral")}>
      {min !== undefined ? formatCurrency(min) : formatCurrency(0)} – {max !== undefined ? formatCurrency(max) : "any"}
    </span>
  );
}