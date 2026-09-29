import Link from "next/link";
import { ArrowRight } from "lucide-react";
import type { ReactNode } from "react";

interface SectionHeaderProps {
  id?: string;
  title: string;
  description?: string;
  actionHref?: string;
  actionLabel?: string;
  action?: ReactNode;
}

/**
 * The one heading pattern used by storefront sections.
 *
 * A section heading is never just styled text: the action is a real link with an icon rather
 * than a Unicode arrow, and the accessible name joins the action to its section.
 */
export function SectionHeader({ id, title, description, actionHref, actionLabel = "View all", action }: SectionHeaderProps) {
  return (
    <div className="mp-section-head">
      <div>
        <h2 className="mp-section-title" id={id} style={{ margin: 0 }}>
          {title}
        </h2>
        {description ? (
          <p style={{ margin: "var(--space-1) 0 0", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{description}</p>
        ) : null}
      </div>

      {action ??
        (actionHref ? (
          <Link className="mp-section-action" href={actionHref} aria-label={`${actionLabel}: ${title}`}>
            {actionLabel}
            <ArrowRight size={14} aria-hidden />
          </Link>
        ) : null)}
    </div>
  );
}
