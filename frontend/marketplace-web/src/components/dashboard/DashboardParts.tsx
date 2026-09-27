/**
 * The pieces a dashboard is made of.
 *
 * A number with a label is the atom here, and it always carries the context that makes it
 * mean something: a period, a comparison, or the words "so far". A figure with no frame is a
 * number somebody has to guess about.
 */

import type { ReactNode } from "react";

/** One figure. `hint` is what stops it being ambiguous. */
export function StatTile({
  label,
  value,
  hint,
  tone = "neutral",
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: "neutral" | "positive" | "warning" | "danger";
}) {
  const colour =
    tone === "positive" ? "var(--success)" : tone === "warning" ? "var(--warning)" : tone === "danger" ? "var(--danger)" : "var(--text)";

  return (
    <div className="mp-card mp-stat" style={{ padding: "var(--space-4)", height: "100%" }}>
      <p className="mp-metric-label" style={{ margin: 0 }}>
        {label}
      </p>
      <p className="mp-stat-value" style={{ color: colour }}>
        {value}
      </p>
      {hint ? (
        <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{hint}</p>
      ) : null}
    </div>
  );
}

export function StatRow({ children, columns = 4 }: { children: ReactNode; columns?: 2 | 3 | 4 }) {
  return <div className={`row g-3 mp-stat-row cols-${columns}`}>{children}</div>;
}

/** A section with a heading, so a dashboard reads as sections rather than a wall of numbers. */
export function Panel({
  title,
  action,
  children,
  className,
}: {
  title: string;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={`mp-card ${className ?? ""}`} style={{ padding: "var(--space-4)" }}>
      <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
        <h2 className="mp-section-title" style={{ fontSize: "var(--fs-h3)" }}>
          {title}
        </h2>
        {action}
      </div>
      {children}
    </section>
  );
}

/** The range picker every analytics panel shares, so the whole page is on one period. */
export function RangePicker({
  value,
  onChange,
  label,
}: {
  value: string;
  onChange: (value: string) => void;
  label: string;
}) {
  return (
    <div className="d-flex align-items-center" style={{ gap: "0.5rem" }}>
      <label htmlFor="range" className="mp-metric-label" style={{ margin: 0 }}>
        {label}
      </label>
      <select
        id="range"
        value={value}
        onChange={event => onChange(event.target.value)}
        style={{
          padding: "0.35rem 0.5rem",
          borderRadius: "var(--radius-sm)",
          border: "1px solid var(--border)",
          backgroundColor: "var(--surface)",
          color: "var(--text)",
          fontSize: "var(--fs-sm)",
        }}
      >
        {RANGE_OPTIONS.map(option => (
          <option key={option.id} value={option.id}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

const RANGE_OPTIONS = [
  { id: "Last7Days", label: "Last 7 days" },
  { id: "Last30Days", label: "Last 30 days" },
  { id: "Last90Days", label: "Last 90 days" },
  { id: "ThisMonth", label: "This month" },
  { id: "LastMonth", label: "Last month" },
  { id: "ThisYear", label: "This year" },
];
