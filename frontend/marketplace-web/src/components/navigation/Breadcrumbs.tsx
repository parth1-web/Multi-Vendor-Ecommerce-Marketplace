import Link from "next/link";
import { ChevronRight } from "lucide-react";

export interface BreadcrumbTrailItem {
  name: string;
  href?: string;
}

/**
 * One breadcrumb implementation for every listing page.
 *
 * The trail is data, not markup: callers pass Home plus their context, and the last item is the
 * current page. Long names are truncated with CSS on narrow screens while the accessible name
 * stays whole, so nothing is lost to make room.
 */
export function Breadcrumbs({ trail, label = "Breadcrumb" }: { trail: BreadcrumbTrailItem[]; label?: string }) {
  return (
    <nav aria-label={label} className="mb-2">
      <ol className="list-unstyled mp-breadcrumbs">
        {trail.map((crumb, index) => {
          const last = index === trail.length - 1;

          return (
            <li key={crumb.href ?? crumb.name} className="d-flex align-items-center" style={{ gap: "0.35rem", minWidth: 0 }}>
              {index > 0 ? <ChevronRight size={12} aria-hidden style={{ flex: "none" }} /> : null}
              {last || !crumb.href ? (
                <span aria-current={last ? "page" : undefined} style={last ? { color: "var(--text)" } : undefined}>
                  {crumb.name}
                </span>
              ) : (
                <Link href={crumb.href}>{crumb.name}</Link>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
