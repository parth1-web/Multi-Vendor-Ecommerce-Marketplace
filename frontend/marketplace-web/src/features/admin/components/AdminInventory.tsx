"use client";

/**
 * Every seller's stock in one list.
 *
 * The same endpoint the seller workspace uses, read as an administrator: the service drops its
 * seller predicate for an admin, so one call returns every store's rows. That is the reason this
 * screen exists — a seller sees their own stock, and nobody could previously see all of it at once.
 *
 * Two things the contract does not provide, and this screen does not fake. There is **no seller
 * filter**, and the response carries **no store name**, so the store beside each row is joined
 * from the product list rather than asked for — and where that join finds nothing, the cell says
 * so instead of showing a wrong store.
 *
 * An adjustment here is the seller's own operation, reached by an administrator: a signed delta
 * with a required reason, recorded in the ledger and returned as the row as it now stands. It is
 * confirmed first, because stock corrected from the wrong row is a support ticket later.
 */

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { History, Search } from "lucide-react";

import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { TextField } from "@/components/forms/FormField";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { formatDateTime, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { InventoryItem, InventoryTransaction, InventoryTransactionType } from "@/types/seller";

const BASE_PATH = "/admin/inventory";

const TRANSACTION_LABELS: Record<InventoryTransactionType, string> = {
  InitialStock: "Opening stock",
  Restock: "Restocked",
  Reservation: "Held for a basket",
  ReservationRelease: "Basket released",
  Sale: "Sold",
  SaleReversal: "Sale reversed",
  ManualAdjustment: "Manual adjustment",
  Return: "Returned to stock",
  Damage: "Removed as damaged",
};

export function AdminInventory() {
  const { push } = useToast();

  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState<"" | "low" | "out">("");
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [adjusting, setAdjusting] = useState<InventoryItem | null>(null);
  const [historyFor, setHistoryFor] = useState<InventoryItem | null>(null);

  const inventory = useQuery({
    queryKey: queryKeys.admin.platformInventory({ page, filter, term }),
    queryFn: () =>
      adminApi.inventory({
        page,
        search: term || undefined,
        lowStockOnly: filter === "low",
        outOfStockOnly: filter === "out",
      }),
  });

  /*
   * The store a row belongs to, joined rather than fetched.
   *
   * `InventoryItemResponse` carries a product id and no seller, so the only way to name the store
   * is to ask the product list — and the admin product list returns every product with its
   * `storeName` on the same page. It is one extra request for the whole screen rather than one per
   * row, and it is what makes a platform-wide list readable at all.
   */
  const storeNames = useQuery({
    queryKey: queryKeys.admin.products({ page: 1, pageSize: 100 }),
    queryFn: () => adminApi.moderationQueue({ page: 1, pageSize: 100 }),
    staleTime: 60_000,
  });

  const storeByProduct = new Map((storeNames.data?.items ?? []).map(product => [product.id, product.storeName || product.sellerName]));

  const filtered = filter !== "" || term !== "";

  return (
    <div className="mp-stack">
      <form
        role="search"
        aria-label="Search and filter stock"
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        onSubmit={event => {
          event.preventDefault();
          setTerm(search.trim());
          setPage(1);
        }}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-sm-6 col-md-5">
            <label htmlFor="platform-stock-search" className="mp-metric-label">
              Search stock
            </label>
            <input
              id="platform-stock-search"
              type="search"
              className="form-control form-control-sm"
              value={search}
              placeholder="Product name or SKU"
              onChange={event => setSearch(event.target.value)}
              autoComplete="off"
            />
          </div>

          <div className="col-12 col-sm-6 col-md-4">
            <span className="mp-metric-label" id="platform-stock-filter">
              Show
            </span>
            <div className="d-flex" style={{ gap: "0.35rem" }} role="group" aria-labelledby="platform-stock-filter">
              {[
                { value: "", label: "All" },
                { value: "low", label: "Low stock" },
                { value: "out", label: "Out of stock" },
              ].map(option => (
                <button
                  key={option.value || "all"}
                  type="button"
                  className={filter === option.value ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
                  aria-pressed={filter === option.value}
                  onClick={() => {
                    setFilter(option.value as "" | "low" | "out");
                    setPage(1);
                  }}
                >
                  {option.label}
                </button>
              ))}
            </div>
          </div>

          <div className="col-12 col-md-3">
            <button type="submit" className="btn btn-sm btn-outline-secondary w-100">
              <Search size={14} aria-hidden className="me-1" />
              Search
            </button>
          </div>
        </div>
      </form>

      {inventory.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />
      ) : inventory.isError || !inventory.data ? (
        <ErrorState message="We could not load the marketplace's stock." onRetry={() => void inventory.refetch()} />
      ) : inventory.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No stock matches those filters" : "No stock records yet"}
          body={
            filtered
              ? "Try another filter, or clear the search to see every store's stock."
              : "Stock appears once sellers have listings with variants."
          }
          action={
            filtered ? (
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => {
                  setFilter("");
                  setTerm("");
                  setPage(1);
                }}
              >
                Show all stock
              </button>
            ) : undefined
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(inventory.data.totalCount)} {inventory.data.totalCount === 1 ? "variant" : "variants"} across
            every store
            {inventory.data.totalPages > 1 ? ` · page ${inventory.data.page} of ${inventory.data.totalPages}` : ""}
          </p>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Stock across every store</caption>
              <thead>
                <tr>
                  <th scope="col">Product</th>
                  <th scope="col">Store</th>
                  <th scope="col">SKU</th>
                  <th scope="col" className="text-end">
                    On hand
                  </th>
                  <th scope="col" className="text-end">
                    Held
                  </th>
                  <th scope="col" className="text-end">
                    Sellable
                  </th>
                  <th scope="col">Status</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {inventory.data.items.map(item => (
                  <tr key={item.inventoryId}>
                    <td>
                      {item.productName}
                      <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                        {item.variantName}
                      </span>
                    </td>
                    <td style={{ fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
                      {storeByProduct.get(item.productId) ?? "—"}
                    </td>
                    <td style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>{item.sku}</td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                      {item.availableQuantity}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                      {item.reservedQuantity}
                    </td>
                    <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                      {item.sellableQuantity}
                    </td>
                    <td>
                      {item.isOutOfStock ? (
                        <StatusBadge tone="danger">Out of stock</StatusBadge>
                      ) : item.isLowStock ? (
                        <StatusBadge tone="warning">Low</StatusBadge>
                      ) : (
                        <StatusBadge tone="success">In stock</StatusBadge>
                      )}
                    </td>
                    <td>
                      <div className="d-flex justify-content-end" style={{ gap: "0.35rem" }}>
                        <button
                          type="button"
                          className="btn btn-sm btn-outline-secondary"
                          onClick={() => setAdjusting(item)}
                          aria-label={`Adjust stock for ${item.productName}, ${item.variantName}`}
                        >
                          Adjust
                        </button>
                        <button
                          type="button"
                          className="btn btn-sm btn-outline-secondary"
                          onClick={() => setHistoryFor(item)}
                          aria-label={`Show stock history for ${item.productName}, ${item.variantName}`}
                          title="Stock history"
                        >
                          <History size={14} aria-hidden />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination page={inventory.data.page} totalPages={inventory.data.totalPages} query={{ filter, search: term }} basePath={BASE_PATH} />
        </>
      )}

      {adjusting ? (
        <AdjustStockDialog
          item={adjusting}
          storeName={storeByProduct.get(adjusting.productId) ?? null}
          onClose={() => setAdjusting(null)}
          onDone={(updated, delta) => {
            setAdjusting(null);
            push({
              tone: "success",
              title: delta > 0 ? "Stock added" : "Stock removed",
              body: `${updated.sku} now has ${updated.availableQuantity} on hand and ${updated.sellableQuantity} sellable.`,
            });
          }}
        />
      ) : null}

      {historyFor ? <HistoryDialog item={historyFor} onClose={() => setHistoryFor(null)} /> : null}
    </div>
  );
}

/**
 * Correct stock from the admin console.
 *
 * The value is a signed change, not a new total — the API adds it to what is on hand — and that is
 * said on the field, because an administrator who enters "12" meaning "twelve left" would otherwise
 * add twelve. The reason is required by the API and is the only thing that makes the ledger entry
 * explainable at stock-take time. The figures shown afterwards are the API's own returned row.
 */
function AdjustStockDialog({
  item,
  storeName,
  onClose,
  onDone,
}: {
  item: InventoryItem;
  storeName: string | null;
  onClose: () => void;
  onDone: (item: InventoryItem, delta: number) => void;
}) {
  const queryClient = useQueryClient();
  const [delta, setDelta] = useState("");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);

  const adjust = useMutation({
    mutationFn: () => adminApi.adjustStock(item.productVariantId, Number(delta), reason.trim()),
    onSuccess: async updated => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.platformInventory({})[0] }),
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.platformInventoryTransactions(item.productVariantId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.summary() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.products }),
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.reports() }),
        // Stock moved, so the analytical stock report is stale — including its low-stock filter,
        // which is defined in terms of what is sellable.
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.inventoryReport({})[0] }),
      ]);
      onDone(updated, Number(delta));
    },
    onError: failure => setError(errorMessage(failure)),
  });

  const parsed = Number(delta);
  const deltaError =
    submitted && (delta.trim() === "" || !Number.isInteger(parsed) || parsed === 0)
      ? "Enter a whole number, other than zero."
      : undefined;
  const removeError =
    submitted && Number.isInteger(parsed) && parsed < 0 && Math.abs(parsed) > item.availableQuantity
      ? `You can only remove ${item.availableQuantity}.`
      : undefined;
  const reasonError = submitted && reason.trim().length === 0 ? "A reason is required for every adjustment." : undefined;

  return (
    <ConfirmDialog
      show
      title={`Adjust stock for ${item.sku}`}
      confirmLabel="Save adjustment"
      cancelLabel="Leave stock alone"
      tone="primary"
      busy={adjust.isPending}
      onCancel={onClose}
      onConfirm={() => {
        setSubmitted(true);
        setError(null);

        if (deltaError || removeError || reasonError) {
          return;
        }

        adjust.mutate();
      }}
    >
      <p className="mb-1">
        {item.productName} · {item.variantName}
      </p>
      {storeName ? (
        <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{storeName}</p>
      ) : null}

      <dl className="row g-2 my-3" style={{ fontSize: "var(--fs-sm)" }}>
        <div className="col-4">
          <dt className="mp-metric-label">On hand</dt>
          <dd className="mb-0">{item.availableQuantity}</dd>
        </div>
        <div className="col-4">
          <dt className="mp-metric-label">Held</dt>
          <dd className="mb-0">{item.reservedQuantity}</dd>
        </div>
        <div className="col-4">
          <dt className="mp-metric-label">Sellable</dt>
          <dd className="mb-0">{item.sellableQuantity}</dd>
        </div>
      </dl>

      <div className="mp-stack-sm">
        <TextField
          label="Change"
          required
          type="number"
          inputMode="numeric"
          step="1"
          placeholder="e.g. 10 or -2"
          hint="A positive number adds stock, a negative number removes it. This is a change, not a new total."
          value={delta}
          onChange={event => setDelta(event.target.value)}
          error={deltaError ?? removeError}
        />

        <TextField
          label="Reason"
          required
          maxLength={300}
          placeholder="Delivery arrived, stock-take correction"
          hint="Recorded against the movement, and shown to the seller."
          value={reason}
          onChange={event => setReason(event.target.value)}
          error={reasonError}
        />

        {error ? (
          <p role="alert" className="mp-alert mp-alert-danger mb-0">
            {error}
          </p>
        ) : null}
      </div>

      <p className="visually-hidden" aria-live="polite">
        {adjust.isPending ? "Saving the adjustment." : ""}
      </p>
    </ConfirmDialog>
  );
}

/** The API's own ledger for one variant, across every store. */
function HistoryDialog({ item, onClose }: { item: InventoryItem; onClose: () => void }) {
  const queryClient = useQueryClient();
  const transactions = useQuery({
    queryKey: queryKeys.admin.platformInventoryTransactions(item.productVariantId),
    queryFn: () => adminApi.inventoryTransactions(item.productVariantId, 50),
  });

  // The ledger is fetched on open, so it is worth clearing on close rather than leaving a
  // stale copy behind for the next variant with the same id pattern in a long session.
  function close() {
    onClose();
    void queryClient.invalidateQueries({ queryKey: queryKeys.admin.platformInventoryTransactions(item.productVariantId) });
  }

  return (
    <ConfirmDialog
      show
      title={`Stock history for ${item.sku}`}
      confirmLabel="Close"
      cancelLabel="Close"
      tone="primary"
      onCancel={close}
      onConfirm={close}
    >
      <p className="mb-3">
        {item.productName} · {item.variantName} · {item.availableQuantity} on hand now
      </p>

      {transactions.isPending ? (
        <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius-sm)" }} aria-hidden />
      ) : transactions.isError ? (
        <ErrorState
          message="We could not load the history for this variant."
          onRetry={() => void transactions.refetch()}
        />
      ) : transactions.data.length === 0 ? (
        <p style={{ margin: 0, color: "var(--text-muted)" }}>
          Nothing has moved for this variant yet. Sales and adjustments appear here as they happen.
        </p>
      ) : (
        <ul className="list-unstyled mb-0" style={{ fontSize: "var(--fs-sm)" }}>
          {transactions.data.map(entry => (
            <HistoryRow key={entry.id} entry={entry} />
          ))}
        </ul>
      )}
    </ConfirmDialog>
  );
}

function HistoryRow({ entry }: { entry: InventoryTransaction }) {
  const added = entry.quantityDelta > 0;

  return (
    <li style={{ padding: "0.5rem 0", borderTop: "1px solid var(--border)" }}>
      <div className="d-flex justify-content-between align-items-baseline" style={{ gap: "0.5rem" }}>
        <span style={{ fontWeight: 500 }}>{TRANSACTION_LABELS[entry.type] ?? entry.type}</span>
        <span style={{ fontVariantNumeric: "tabular-nums", color: added ? "var(--success)" : "var(--text-muted)" }}>
          {added ? "+" : ""}
          {entry.quantityDelta}
        </span>
      </div>
      <div className="d-flex justify-content-between" style={{ fontSize: "var(--fs-xs)", color: "var(--text-subtle)" }}>
        <span>
          {entry.quantityBefore} → {entry.quantityAfter} units
        </span>
        <span>{formatDateTime(entry.createdAt)}</span>
      </div>
      {entry.reason ? (
        <div style={{ fontSize: "var(--fs-xs)", color: "var(--text-muted)" }}>{entry.reason}</div>
      ) : null}
    </li>
  );
}