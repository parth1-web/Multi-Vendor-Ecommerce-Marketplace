/**
 * Page links for a listing.
 *
 * Written to the URL rather than to component state, so a page is shareable, bookmarkable and
 * reachable with the back button — and so the server can render it from the path alone. Shared by
 * the catalogue and the directory of stores, because "Previous / Page 3 of 9 / Next" is the same
 * control and there is no reason to draw it twice.
 */

import Link from "next/link";

import { listingHref } from "@/features/products/api/readProductQuery";
import { cx } from "@/lib/format";

interface PaginationProps {
  page: number;
  totalPages: number;
  /** The filters to carry across, so paging does not quietly drop them. */
  query: object;
  /** Where this listing lives, so a page of stores does not turn into a page of products. */
  basePath: string;
}

export function Pagination({ page, totalPages, query, basePath }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  return (
    <nav aria-label="Pagination" className="d-flex justify-content-between align-items-center mt-4">
      <Link
        className={cx("btn btn-sm btn-outline-secondary", page <= 1 && "disabled")}
        href={listingHref(basePath, query, Math.max(1, page - 1))}
        aria-disabled={page <= 1}
        scroll={false}
      >
        Previous
      </Link>

      <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        Page {page} of {totalPages}
      </span>

      <Link
        className={cx("btn btn-sm btn-outline-secondary", page >= totalPages && "disabled")}
        href={listingHref(basePath, query, Math.min(totalPages, page + 1))}
        aria-disabled={page >= totalPages}
        scroll={false}
      >
        Next
      </Link>
    </nav>
  );
}
