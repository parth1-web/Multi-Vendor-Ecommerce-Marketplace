/**
 * The frame every signed-out page shares.
 *
 * Sign in, register and reset are recognisably the same place: same width, same card, same
 * invitation at the bottom to the one you did not pick. It is a component rather than a copy in
 * each page because three slightly different panels is how a set stops looking like a set.
 */

import type { ReactNode } from "react";

export function AuthPanel({
  title,
  subtitle,
  children,
  footer,
}: {
  title: string;
  subtitle: string;
  children: ReactNode;
  footer?: ReactNode;
}) {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-6)" }}>
      <div className="row justify-content-center">
        <div className="col-12 col-md-8 col-lg-5">
          <div className="mp-card" style={{ padding: "var(--space-5)" }}>
            <h1 className="mp-section-title" style={{ fontSize: "var(--fs-h2)" }}>
              {title}
            </h1>
            <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{subtitle}</p>

            {children}
          </div>

          {footer ? (
            <p style={{ textAlign: "center", marginTop: "var(--space-4)", fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
              {footer}
            </p>
          ) : null}
        </div>
      </div>
    </div>
  );
}

export function AuthPanelSkeleton() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-6)" }}>
      <div className="row justify-content-center">
        <div className="col-12 col-md-8 col-lg-5">
          <div className="mp-card mp-skeleton" style={{ height: "22rem", padding: "var(--space-5)" }} />
        </div>
      </div>
    </div>
  );
}
