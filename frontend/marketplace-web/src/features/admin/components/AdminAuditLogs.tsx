"use client";

/**
 * The audit log: who changed what, when, and what it changed to.
 *
 * Built on what `GET /api/admin/audit-logs` returns and accepts — `action`, `entityType`,
 * `entityId`, `actorId`, `from`, `to`, `search`, all applied and paged by the server. Nothing is
 * filtered in the browser: a client-side filter over one page of twenty-five rows out of a few
 * hundred is a search that lies, because it claims to describe the log while describing a page.
 *
 * Three things the log is *not*, kept visible because each one looks like data:
 *
 * - The action names are the API's `AuditAction` enum, spelled as the enum spells them. No
 *   friendlier paraphrase is invented, because a paraphrase of `ReviewModerated` can differ from
 *   it in a way that matters when somebody is reading the log as evidence.
 * - A row is only present if a service chose to write one. Actions that change data without
 *   recording it are absent, and the gaps are listed under the table rather than papered over.
 * - The payload is the server's own redacted JSON. Passwords, tokens and card numbers are
 *   stripped before the row is written; this page shows what arrived rather than deciding what
 *   looks sensitive after the fact.
 *
 * There is no export button, because there is no audit export endpoint. Building a CSV from the
 * page on screen would produce a file that looks like the log and is not.
 */

import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, ChevronRight } from "lucide-react";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { adminApi } from "@/features/admin/api/adminApi";
import { cx, formatDateTime, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { AUDIT_ACTIONS, type AuditActionName, type AuditEntry } from "@/types/admin";

const BASE_PATH = "/admin/audit-logs";
const PAGE_SIZE = 25;

export function AdminAuditLogs() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const page = Math.max(1, Number(searchParams.get("page") ?? "1") || 1);
  const action = (searchParams.get("action") ?? "") as AuditActionName | "";
  const term = searchParams.get("search") ?? "";
  const entityId = searchParams.get("entityId") ?? "";
  const from = searchParams.get("from") ?? "";
  const to = searchParams.get("to") ?? "";

  // The box is seeded from the URL and re-seeded whenever the URL changes underneath it, so what
  // is typed and what is searched can never disagree.
  const [search, setSearch] = useState(term);
  const searchKey = searchParams.get("search") ?? "";
  const [fromInput, setFromInput] = useState(from);
  const [toInput, setToInput] = useState(to);
  const dateKey = `${from}|${to}`;

  const navigate = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(searchParams.toString());

    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    // Any filter change starts again at page one, or a narrower result set can land the reader on
    // a page that does not exist.
    next.delete("page");

    const query = next.toString();
    router.replace(query ? `${BASE_PATH}?${query}` : BASE_PATH, { scroll: false });
  };

  const filters = { page, action, search: searchKey, entityId, from, to };

  const logs = useQuery({
    queryKey: queryKeys.admin.auditLogs(filters),
    queryFn: () =>
      adminApi.auditLogs({
        page,
        pageSize: PAGE_SIZE,
        action: action || undefined,
        search: searchKey || undefined,
        entityId: entityId || undefined,
        from: from ? startOfDay(from) : undefined,
        to: to ? endOfDay(to) : undefined,
      }),
  });

  const clearAll = () => {
    setSearch("");
    setFromInput("");
    setToInput("");
    router.replace(BASE_PATH, { scroll: false });
  };

  const filtered = Boolean(action || searchKey || entityId || from || to);

  return (
    <div className="mp-stack">
      <form
        role="search"
        aria-label="Search and filter the audit log"
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        onSubmit={event => {
          event.preventDefault();
          navigate({ search: search.trim() || undefined });
        }}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-sm-6 col-md-4">
            <label htmlFor="audit-search" className="mp-metric-label">
              Search
            </label>
            <input
              key={`search-${searchKey}`}
              id="audit-search"
              type="search"
              className="form-control form-control-sm"
              value={search}
              placeholder="An actor's email or the name of what changed"
              onChange={event => setSearch(event.target.value)}
              autoComplete="off"
            />
          </div>

          <div className="col-12 col-sm-6 col-md-3">
            <label htmlFor="audit-action" className="mp-metric-label">
              Action
            </label>
            <select
              id="audit-action"
              className="form-select form-select-sm"
              value={action}
              onChange={event => navigate({ action: event.target.value || undefined })}
            >
              <option value="">Every action</option>
              {AUDIT_ACTIONS.map(value => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </div>

          <div className="col-6 col-sm-3 col-md-2">
            <label htmlFor="audit-from" className="mp-metric-label">
              From
            </label>
            <input
              key={`from-${dateKey}`}
              id="audit-from"
              type="date"
              className="form-control form-control-sm"
              value={fromInput}
              onChange={event => {
                setFromInput(event.target.value);
                navigate({ from: event.target.value || undefined });
              }}
            />
          </div>

          <div className="col-6 col-sm-3 col-md-2">
            <label htmlFor="audit-to" className="mp-metric-label">
              To
            </label>
            <input
              key={`to-${dateKey}`}
              id="audit-to"
              type="date"
              className="form-control form-control-sm"
              value={toInput}
              onChange={event => {
                setToInput(event.target.value);
                navigate({ to: event.target.value || undefined });
              }}
            />
          </div>

          <div className="col-12 col-md-1">
            {filtered ? (
              <button type="button" className="btn btn-sm btn-outline-secondary w-100" onClick={clearAll}>
                Clear
              </button>
            ) : null}
          </div>
        </div>

        <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
          Search matches the actor&apos;s email and the target&apos;s name — the two fields the API searches. It does not look
          inside the payload.
        </p>
      </form>

      {entityId ? (
        <p className="mp-alert" style={{ margin: 0, display: "flex", alignItems: "center", gap: "var(--space-3)", flexWrap: "wrap" }}>
          <span>
            Showing only events for one target{" "}
            <code style={{ fontSize: "0.85em" }}>{entityId}</code>.
          </span>
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => navigate({ entityId: undefined })}>
            Show every target
          </button>
        </p>
      ) : null}

      {logs.isPending ? (
        <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : logs.isError || !logs.data ? (
        <ErrorState message="We could not load the audit log." onRetry={() => void logs.refetch()} />
      ) : logs.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No events match those filters" : "Nothing recorded yet"}
          body={
            filtered
              ? "Try a wider date range, or clear the filters to see the whole log."
              : "Anything an account, a moderator or a payment does that matters is written here."
          }
          action={
            filtered ? (
              <button type="button" className="btn btn-sm btn-primary" onClick={clearAll}>
                Show the whole log
              </button>
            ) : undefined
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(logs.data.totalCount)} {logs.data.totalCount === 1 ? "event" : "events"}
            {logs.data.totalPages > 1 ? ` · page ${logs.data.page} of ${logs.data.totalPages}` : ""}
          </p>

          <AuditTable entries={logs.data.items} onFocusTarget={id => navigate({ entityId: id })} />
        </>
      )}

      {logs.data && logs.data.totalPages > 1 ? (
        <Pagination
          page={logs.data.page}
          totalPages={logs.data.totalPages}
          query={{ action, search: searchKey, entityId, from, to }}
          basePath={BASE_PATH}
        />
      ) : null}

      <CoverageNote />
    </div>
  );
}

/**
 * One row per event, with the detail behind an expandable row.
 *
 * A table row cannot hold a definition list, so the detail is a second row spanning the columns —
 * which keeps the reading order intact for a screen reader and keeps the keyboard behaviour of a
 * plain button.
 */
function AuditTable({ entries, onFocusTarget }: { entries: AuditEntry[]; onFocusTarget: (id: string) => void }) {
  const [open, setOpen] = useState<string | null>(null);

  return (
    <div className="mp-table-wrap">
      <table className="mp-table">
        <caption className="visually-hidden">Audit events, newest first</caption>
        <thead>
          <tr>
            <th scope="col">When</th>
            <th scope="col">Actor</th>
            <th scope="col">Action</th>
            <th scope="col">Target</th>
            <th scope="col">
              <span className="visually-hidden">Details</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {entries.map(entry => {
            const expanded = open === entry.id;

            return (
              <tr key={entry.id}>
                <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", whiteSpace: "nowrap" }}>
                  <time dateTime={entry.createdAt}>{formatDateTime(entry.createdAt)}</time>
                </td>
                <td style={{ fontSize: "var(--fs-sm)" }}>
                  {entry.actorEmail}
                  {entry.actorEmail === "anonymous" ? (
                    <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                      nobody signed in yet
                    </span>
                  ) : null}
                </td>
                <td>
                  <StatusBadge tone={actionTone(entry.action)}>{entry.action}</StatusBadge>
                </td>
                <td style={{ fontSize: "var(--fs-sm)" }}>
                  {entry.entityName || <span style={{ color: "var(--text-subtle)" }}>{entry.entityType}</span>}
                  <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{entry.entityType}</span>
                </td>
                <td>
                  <button
                    type="button"
                    className={cx("btn btn-sm", expanded ? "btn-primary" : "btn-outline-secondary")}
                    aria-expanded={expanded}
                    aria-controls={`audit-detail-${entry.id}`}
                    onClick={() => setOpen(expanded ? null : entry.id)}
                  >
                    {expanded ? <ChevronDown size={14} aria-hidden /> : <ChevronRight size={14} aria-hidden />}{" "}
                    {expanded ? "Close" : "Detail"}
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {entries.map(entry =>
        open === entry.id ? (
          <section
            key={`detail-${entry.id}`}
            id={`audit-detail-${entry.id}`}
            className="mp-card"
            style={{ padding: "var(--space-4)" }}
            aria-label={`Detail of the ${entry.action} event`}
          >
            <AuditDetail entry={entry} onFocusTarget={onFocusTarget} />
          </section>
        ) : null
      )}
    </div>
  );
}

/**
 * Everything the row did not have room for.
 *
 * The payload is rendered as key/value pairs from the server's own JSON rather than dumped as a
 * string, because a reader asking "what exactly changed?" wants the field names, and because a
 * malformed payload should show as unreadable rather than throw.
 */
function AuditDetail({ entry, onFocusTarget }: { entry: AuditEntry; onFocusTarget: (id: string) => void }) {
  const changes = parseChanges(entry.changesJson);

  return (
    <div className="mp-stack">
      <dl className="row g-2 mb-0" style={{ fontSize: "var(--fs-sm)" }}>
        <div className="col-6 col-md-3">
          <dt className="mp-metric-label">When</dt>
          <dd className="mb-0">{formatDateTime(entry.createdAt)}</dd>
        </div>
        <div className="col-6 col-md-3">
          <dt className="mp-metric-label">Actor</dt>
          <dd className="mb-0">{entry.actorEmail}</dd>
        </div>
        <div className="col-6 col-md-3">
          <dt className="mp-metric-label">Action</dt>
          <dd className="mb-0">{entry.action}</dd>
        </div>
        <div className="col-6 col-md-3">
          <dt className="mp-metric-label">Request</dt>
          <dd className="mb-0" style={{ fontVariantNumeric: "tabular-nums" }}>
            {entry.correlationId ? entry.correlationId.slice(0, 8) : "—"}
          </dd>
        </div>
        <div className="col-12 col-md-6">
          <dt className="mp-metric-label">Target</dt>
          <dd className="mb-0">
            {entry.entityType}
            {entry.entityName ? ` · ${entry.entityName}` : ""}
            {entry.entityId ? (
              <>
                {" "}
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  style={{ padding: "0 0.4rem" }}
                  onClick={() => onFocusTarget(entry.entityId!)}
                >
                  Only events for this target
                </button>
              </>
            ) : null}
          </dd>
        </div>
        <div className="col-12 col-md-6">
          <dt className="mp-metric-label">IP address</dt>
          <dd className="mb-0">{entry.ipAddress ?? "Not recorded"}</dd>
        </div>
      </dl>

      <div>
        <h3 className="mp-section-title" style={{ margin: 0, fontSize: "var(--fs-body)" }}>
          What changed
        </h3>
        {changes === null ? (
          <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Nothing was recorded with this event. Not every action carries a payload — a sign-in records that it happened.
          </p>
        ) : changes.length === 0 ? (
          <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            The payload is an empty object.
          </p>
        ) : (
          <dl className="row g-2 mb-0 mt-1" style={{ fontSize: "var(--fs-sm)" }}>
            {changes.map(change => (
              <div key={change.key} className="col-12 col-sm-6 col-lg-4">
                <dt className="mp-metric-label">{change.key}</dt>
                <dd className="mb-0" style={{ wordBreak: "break-word" }}>
                  {change.value}
                </dd>
              </div>
            ))}
          </dl>
        )}
      </div>
    </div>
  );
}

/**
 * What the log does not contain, stated where the log is read.
 *
 * An audit log is evidence, and evidence that is quietly incomplete is worse than none: an
 * operator checking whether a threshold was changed finds nothing and concludes it was not. These
 * are the gaps as the code stands, not a to-do list.
 */
function CoverageNote() {
  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="audit-coverage">
      <h2 id="audit-coverage" className="mp-section-title" style={{ margin: 0, fontSize: "var(--fs-body)" }}>
        What this log does not record
      </h2>
      <ul className="mb-0 mt-2" style={{ paddingInlineStart: "1.1rem", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        <li>
          <strong>Stock threshold changes.</strong> Changing a variant&apos;s low-stock threshold writes no event, so a
          threshold that moved cannot be traced here. Stock adjustments themselves are recorded.
        </li>
        <li>
          <strong>Report exports.</strong> The API defines a <code>ReportExported</code> action but never writes it, so
          downloading a report leaves no trace — including the CSV export on the reports page.
        </li>
        <li>
          <strong>Reads.</strong> Only changes are recorded. Somebody looking at an order or a customer&apos;s details
          generates nothing, because there is no event to write.
        </li>
      </ul>
    </section>
  );
}

/* ------------------------------------------------------------------------------- helpers */

/**
 * The server's payload, as key/value pairs.
 *
 * `null` means "there was no payload", which is different from `{}` and is reported differently —
 * a sign-in has nothing to say about what changed, and saying so is more useful than an empty
 * panel. Anything unparseable is shown as one `Payload` row rather than discarded, because a row
 * that cannot be read is exactly the one somebody needs to see.
 */
function parseChanges(changesJson: string | null): { key: string; value: string }[] | null {
  if (!changesJson || !changesJson.trim()) {
    return null;
  }

  let parsed: unknown;

  try {
    parsed = JSON.parse(changesJson);
  } catch {
    return [{ key: "Payload", value: changesJson }];
  }

  if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
    return [{ key: "Payload", value: String(parsed) }];
  }

  return Object.entries(parsed as Record<string, unknown>).map(([key, value]) => ({
    key,
    value: value === null ? "—" : typeof value === "object" ? JSON.stringify(value) : String(value),
  }));
}

/**
 * The API's `from` is an instant and inclusive at the start, so a date typed into the box becomes
 * midnight at the beginning of that day.
 */
function startOfDay(value: string): string {
  return `${value}T00:00:00.000Z`;
}

/** And `to` is inclusive at the end, so a date means the whole of that day rather than its first instant. */
function endOfDay(value: string): string {
  return `${value}T23:59:59.999Z`;
}

/**
 * Colour by what the action did, so a row can be scanned without reading every name.
 *
 * Only four tones are used and each has a text label beside it, so the colour is a second signal
 * rather than the only one.
 */
function actionTone(action: string): "success" | "warning" | "danger" | "info" {
  if (action.startsWith("LoginFailed") || action.includes("Rejected") || action.includes("Failed") || action.includes("Deleted")) {
    return "danger";
  }

  if (action.includes("Suspended") || action.includes("Moderated") || action.includes("Cancelled") || action.includes("Reversed")) {
    return "warning";
  }

  if (action.includes("Approved") || action.includes("Verified") || action.includes("Paid") || action.includes("Succeeded")) {
    return "success";
  }

  return "info";
}