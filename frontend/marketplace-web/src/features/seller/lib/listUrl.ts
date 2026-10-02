"use client";

/**
 * Reading a seller's list state out of the URL, and writing it back.
 *
 * The three seller lists — products, orders and stock — all filter, search and page, and all three
 * of those are things the API can do. Keeping the state in the URL means a filtered view can be
 * shared with a colleague, survives a refresh, and that the back button returns to the view the
 * seller came from rather than to an unfiltered page one. It is the same contract the public
 * catalogue and the customer's order history already keep, so there is nothing new to learn.
 *
 * One hook rather than three copies of `new URLSearchParams(...)`: a filter that drops the search
 * box on the way to page two is a bug nobody notices until a seller loses an hour in it.
 */

import { useCallback } from "react";
import { useRouter, useSearchParams } from "next/navigation";

export interface SellerListUrl<T extends string = string> {
  status?: T;
  search?: string;
  /** The stock view: "low" or "out", set through a single `filter` parameter. */
  filter?: "low" | "out";
  /** Scopes the list to one product, so a product page can link straight to its own stock. */
  productId?: string;
  page: number;
  /** Rewrites one or more parameters and pushes. Page is dropped unless asked for. */
  navigate: (changes: Record<string, string | undefined>, keepPage?: boolean) => void;
  /** True when anything narrows the list, so an empty result can offer a way back to all of it. */
  filtered: boolean;
}

interface SellerListUrlOptions<T extends string = string> {
  /** The statuses this list accepts. An unknown value is dropped rather than sent to the API. */
  statuses?: readonly T[];
}

/**
 * Generic over the status type on purpose: without it a list of, say, coupons would hand its status
 * to the API as a plain string, and the call site would lose the compile-time check that the chips
 * on screen and the enum the API parses are the same set.
 */
export function useSellerListUrl<T extends string = string>(
  basePath: string,
  options: SellerListUrlOptions<T> = {},
): SellerListUrl<T> {
  const router = useRouter();
  const searchParams = useSearchParams();

  const status = readAllowed(searchParams.get("status"), options.statuses);
  const search = searchParams.get("search")?.trim() || undefined;
  const filter = readAllowed(searchParams.get("filter"), ["low", "out"] as const) as "low" | "out" | undefined;
  const productId = searchParams.get("product")?.trim() || undefined;
  const page = positiveInt(searchParams.get("page"));

  const navigate = useCallback(
    (changes: Record<string, string | undefined>, keepPage = false) => {
      const next = new URLSearchParams(searchParams.toString());

      for (const [key, value] of Object.entries(changes)) {
        if (value === undefined || value === "") {
          next.delete(key);
        } else {
          next.set(key, value);
        }
      }

      if (!keepPage) {
        next.delete("page");
      }

      const queryString = next.toString();
      router.push(queryString ? `${basePath}?${queryString}` : basePath);
    },
    [basePath, router, searchParams],
  );

  return {
    status,
    search,
    filter,
    productId,
    page,
    navigate,
    filtered: Boolean(status || search || filter || productId),
  };
}

/** The href for a page of the current view, carrying the filters that produced it. */
export function listHref(basePath: string, filters: Record<string, string | undefined>, page: number): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      search.set(key, value);
    }
  }

  if (page > 1) {
    search.set("page", String(page));
  }

  const queryString = search.toString();

  return queryString ? `${basePath}?${queryString}` : basePath;
}

/**
 * The same view, as a query string for a link that leaves the page — a row that opens a product,
 * or a back link from a detail page. Carrying the filters means the seller returns to the list
 * they were looking at rather than to the top of an unfiltered one.
 */
export function listContext(filters: Record<string, string | undefined>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      search.set(key, value);
    }
  }

  return search.toString();
}

function readAllowed<T extends string>(raw: string | null, allowed: readonly T[] | undefined): T | undefined {
  if (!raw || !allowed) {
    return undefined;
  }

  return allowed.includes(raw as T) ? (raw as T) : undefined;
}

function positiveInt(raw: string | null): number {
  const parsed = Number(raw);

  return Number.isInteger(parsed) && parsed > 0 ? parsed : 1;
}