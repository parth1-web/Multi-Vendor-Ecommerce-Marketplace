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
import { useCategories } from "@/features/products/api/useProducts";
import type { Category } from "@/types/product";

export interface ProductFiltersProps {
  totalCount: number;
}

export function ProductFilters({ totalCount }: ProductFiltersProps) {
  const router = useRouter();
  const searchParams = useSearchParams();

  // The text box holds what is being typed; the URL holds what is being searched. A keystroke
  // per request would hammer the API and make the results flicker, and the `key` on the input
  // resets the box whenever the URL changes underneath it, so the two can never disagree.
  const [search, setSearch] = useState(searchParams.get("search") ?? "");
  const searchKey = searchParams.get("search") ?? "";

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
    router.push(`/products?${next.toString()}`);
  };

  const current = (key: string) => searchParams.get(key) ?? "";

  // The category tree is fetched here rather than passed in, so it cannot disagree with the
  // listing about what the catalogue actually contains.
  const { data: categories } = useCategories();

  return (
    <aside aria-label="Filters" className="mp-stack">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          apply({ search: search.trim() || undefined });
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
            placeholder="Product or brand"
          />
          <button type="submit" className="btn btn-sm btn-primary">
            Go
          </button>
        </div>
      </form>

      <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
        <legend className="mp-metric-label p-0 mb-2">Category</legend>
        <select
          className="form-select form-select-sm"
          value={current("categorySlug")}
          onChange={(event) => apply({ categorySlug: event.target.value || undefined })}
          aria-label="Category"
        >
          <option value="">All categories</option>
          {flatten(categories ?? []).map((category) => (
            <option key={category.id} value={category.slug}>
              {category.name}
            </option>
          ))}
        </select>
      </fieldset>

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
        {hasFilters(searchParams) ? (
          <button
            type="button"
            className="btn btn-sm btn-link p-0"
            style={{ fontSize: "var(--fs-xs)" }}
            onClick={() => router.push("/products")}
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

export function SortSelect() {
  const router = useRouter();
  const searchParams = useSearchParams();

  return (
    <label className="d-flex align-items-center" style={{ gap: "var(--space-2)", fontSize: "var(--fs-sm)" }}>
      <span className="mp-metric-label">Sort</span>
      <select
        className="form-select form-select-sm"
        style={{ width: "auto" }}
        value={searchParams.get("sort") ?? "Newest"}
        onChange={(event) => {
          const next = new URLSearchParams(searchParams.toString());
          if (event.target.value === "Newest") {
            next.delete("sort");
          } else {
            next.set("sort", event.target.value);
          }
          next.delete("page");
          router.push(`/products?${next.toString()}`);
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

/** Categories arrive as a tree; the filter is a flat list, indented to show the hierarchy. */
function flatten(categories: Category[], depth = 0): Array<Category & { depth: number }> {
  return categories.flatMap((category) => [{ ...category, depth }, ...flatten(category.children ?? [], depth + 1)]);
}

function hasFilters(searchParams: Pick<URLSearchParams, 'has'>): boolean {
  return ["search", "categorySlug", "minPrice", "maxPrice", "minRating", "inStock", "onSale"].some((key) => searchParams.has(key));
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