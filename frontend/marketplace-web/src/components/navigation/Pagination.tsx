/**
 * Page links for a listing.
 *
 * Written to the URL rather than to component state, so a page is shareable, bookmarkable and
 * reachable with the back button — and so the server can render it from the path alone. Shared by
 * the catalogue and the directory of stores, because "Previous / Page 3 of 9 / Next" is the same
 * control and there is no reason to draw it twice.
 *
 * Server pagination is preserved throughout: every link is a navigation that re-queries the API
 * for that page. Nothing here slices a dataset on the client.
 */

import Link from "next/link";

import { listingHref } from "@/features/products/api/readProductQuery";
import { cx } from "@/lib/format";
import type { ProductQuery } from "@/types/product";

interface PaginationProps {
  page: number;
  totalPages: number;
  /** The filters to carry across, so paging does not quietly drop them. */
  query: ProductQuery | Record<string, unknown>;
  /** Where this listing lives, so a page of stores does not turn into a page of products. */
  basePath: string;
}

type PageItem = number | "gap";

export function Pagination({ page, totalPages, query, basePath }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const current = Math.min(Math.max(1, page), totalPages);

  return (
    <nav
      aria-label="Pagination"
      className="d-flex align-items-center justify-content-center flex-wrap mt-4"
      style={{ gap: "var(--space-2)" }}
    >
      <Link
        className={cx("btn btn-sm btn-outline-secondary", current <= 1 && "disabled")}
        href={listingHref(basePath, query as Record<string, unknown>, Math.max(1, current - 1))}
        aria-disabled={current <= 1}
        aria-label="Go to previous page"
        scroll={false}
      >
        Previous
      </Link>

      {pageItems(current, totalPages).map((item, index) =>
        item === "gap" ? (
          <span key={`gap-${index}`} aria-hidden style={{ color: "var(--text-subtle)", fontSize: "var(--fs-sm)" }}>
            …
          </span>
        ) : item === current ? (
          <span key={item} className="btn btn-sm btn-primary" aria-current="page" aria-label={`Page ${item}, current page`}>
            {item}
          </span>
        ) : (
          <Link
            key={item}
            className="btn btn-sm btn-outline-secondary"
            href={listingHref(basePath, query as Record<string, unknown>, item)}
            aria-label={`Go to page ${item}`}
            scroll={false}
          >
            {item}
          </Link>
        ),
      )}

      <Link
        className={cx("btn btn-sm btn-outline-secondary", current >= totalPages && "disabled")}
        href={listingHref(basePath, query as Record<string, unknown>, Math.min(totalPages, current + 1))}
        aria-disabled={current >= totalPages}
        aria-label="Go to next page"
        scroll={false}
      >
        Next
      </Link>
    </nav>
  );
}

/**
 * The numbered window: first page, last page, and the current page with its neighbours.
 * Ellipses mark the jump rather than pretending every page is listed.
 */
function pageItems(page: number, totalPages: number): PageItem[] {
  if (totalPages <= 7) {
    return Array.from({ length: totalPages }, (_, index) => index + 1);
  }

  const window = new Set<number>([1, 2, page - 1, page, page + 1, totalPages - 1, totalPages]);
  const pages = [...window].filter((candidate) => candidate >= 1 && candidate <= totalPages).sort((a, b) => a - b);

  const items: PageItem[] = [];

  for (let index = 0; index < pages.length; index += 1) {
    if (index > 0 && pages[index] - pages[index - 1] > 1) {
      items.push("gap");
    }

    items.push(pages[index]);
  }

  return items;
}
