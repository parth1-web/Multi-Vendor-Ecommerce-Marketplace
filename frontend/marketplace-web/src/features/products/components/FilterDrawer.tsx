"use client";

/**
 * The mobile filter drawer.
 *
 * Small screens get no permanent sidebar; the same filter panel lives behind an explicit
 * control instead. React-Bootstrap's offcanvas supplies the accessible parts — focus trap,
 * Escape to close, backdrop dismissal, and body scroll locking — while the footer reports the
 * live result count so opening the drawer never feels like leaving the results behind.
 */

import { useState } from "react";
import { Offcanvas } from "react-bootstrap";
import { SlidersHorizontal } from "lucide-react";

import { ProductFilters } from "@/features/products/components/ProductFilters";

interface FilterDrawerProps {
  basePath?: string;
  searchParamName?: "search" | "q";
  lockedCategorySlug?: string;
  totalCount: number;
  activeCount: number;
}

export function FilterDrawer({
  basePath = "/products",
  searchParamName = "search",
  lockedCategorySlug,
  totalCount,
  activeCount,
}: FilterDrawerProps) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        className="btn btn-outline-secondary"
        onClick={() => setOpen(true)}
        aria-haspopup="dialog"
      >
        <SlidersHorizontal size={16} aria-hidden />
        Filters
        {activeCount > 0 ? (
          <span className="mp-filter-count" aria-label={`${activeCount} filters applied`}>
            {activeCount}
          </span>
        ) : null}
      </button>

      <Offcanvas show={open} onHide={() => setOpen(false)} placement="start" aria-labelledby="filter-drawer-title">
        <Offcanvas.Header closeButton>
          <Offcanvas.Title id="filter-drawer-title">Filters</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>
          <ProductFilters
            totalCount={totalCount}
            basePath={basePath}
            searchParamName={searchParamName}
            lockedCategorySlug={lockedCategorySlug}
          />
        </Offcanvas.Body>
        <div className="mp-drawer-footer">
          <button type="button" className="btn btn-primary w-100" onClick={() => setOpen(false)}>
            Show {totalCount} {totalCount === 1 ? "product" : "products"}
          </button>
        </div>
      </Offcanvas>
    </>
  );
}
