import type { ReactNode } from "react";

import { Breadcrumbs, type BreadcrumbTrailItem } from "@/components/navigation/Breadcrumbs";

interface ListingHeaderProps {
  breadcrumbs: BreadcrumbTrailItem[];
  title: string;
  subtitle?: string;
  actions?: ReactNode;
}

/**
 * The compact header shared by catalogue, search, category, and store listings.
 *
 * Context first, then the page name: a shopper arriving from a link should know where they are
 * before the filters ask anything of them. The result count lives with the results island,
 * which is the only part of the page that knows it.
 */
export function ListingHeader({ breadcrumbs, title, subtitle, actions }: ListingHeaderProps) {
  return (
    <div className="mp-page-header">
      <div style={{ minWidth: 0 }}>
        <Breadcrumbs trail={breadcrumbs} />
        <h1 className="mp-page-title">{title}</h1>
        {subtitle ? <p className="mp-page-subtitle">{subtitle}</p> : null}
      </div>
      {actions}
    </div>
  );
}
