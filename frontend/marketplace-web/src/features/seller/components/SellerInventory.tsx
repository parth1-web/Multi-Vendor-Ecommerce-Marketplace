"use client";

/**
 * A seller's stock, and the three operations the API actually allows on it.
 *
 * The page exists to answer one question — can I still sell this? — so it puts what is on hand next
 * to what is already promised to a basket. They are different numbers: stock held for somebody's
 * basket is not available to anyone else, and a list showing only the first one tells a seller
 * they have more than they do.
 *
 * Everything below the table is a real endpoint. An adjustment is a signed change with a required
 * reason, recorded in the ledger and returned as the row as it now stands; a threshold is the
 * level at or below which the variant counts as low; and the history is the API's own movement
 * ledger for that variant. Nothing here forecasts stock or scores it, because the API does not.
 *
 * Filters, search and page live in the URL, so "my low stock" is a link a seller can bookmark or
 * paste to somebody else, and the product page can link here already scoped to one product.
 */

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { History, SlidersHorizontal } from "lucide-react";

import { TextField } from "@/components/forms/FormField";
import { Pagination } from "@/components/navigation/Pagination";
import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { useSellerListUrl } from "@/features/seller/lib/listUrl";
import { errorMessage } from "@/lib/errors";
import { formatDateTime, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { InventoryItem, InventoryTransaction, InventoryTransactionType } from "@/types/seller";

const BASE_PATH = "/seller/inventory";

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

export function SellerInventory() {
  const { search, filter, productId, page, navigate, filtered } = useSellerListUrl(BASE_PATH);
  const { push } = useToast();

  const [adjusting, setAdjusting] = useState<InventoryItem | null>(null);
  const [thresholding, setThresholding] = useState<InventoryItem | null>(null);
  const [historyFor, setHistoryFor] = useState<InventoryItem | null>(null);

  const inventory = useQuery({
    queryKey: queryKeys.seller.inventory({ page, search, filter, productId }),
    queryFn: () =>
      sellerApi.inventory({
        page,
        search,
        lowStockOnly: filter === "low",
        outOfStockOnly: filter === "out",
        productId,
      }),
  });

  const filters = { search, filter, product: productId };

  return (
    <div className="mp-stack">
      <StockToolbar
        search={search ?? ""}
        filter={filter ?? ""}
        productId={productId}
        onSearch={value => navigate({ search: value.trim() || undefined })}
        onFilter={value => navigate({ filter: value || undefined })}
      />

      {inventory.isPending ? (
        <StockSkeleton />
      ) : inventory.isError || !inventory.data ? (
        <ErrorState message="We could not load your stock." onRetry={() => void inventory.refetch()} />
      ) : inventory.data.items.length === 0 ? (
        <EmptyState
          title={emptyTitle(filter, search, productId)}
          body={emptyBody(filter, search, productId)}
          action={
            filtered ? (
              <Link href={BASE_PATH} className="btn btn-sm btn-primary">
                Show all stock
              </Link>
            ) : (
              <Link href="/seller/products" className="btn btn-sm btn-primary">
                Go to your products
              </Link>
            )
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(inventory.data.totalCount)}{" "}
            {inventory.data.totalCount === 1 ? "variant" : "variants"}
            {inventory.data.totalPages > 1 ? ` · page ${inventory.data.page} of ${inventory.data.totalPages}` : ""}
          </p>

          <div className="mp-table-wrap">
            <table className="mp-table">
              <caption className="visually-hidden">Stock by variant</caption>
              <thead>
                <tr>
                  <th scope="col">Product</th>
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
                  <th scope="col" className="text-end">
                    Low at
                  </th>
                  <th scope="col">Status</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {inventory.data.items.map(item => {
                  /*
                    A stock row whose product no longer exists comes back from the API with no name
                    and no SKU — the record outlived the listing. It is still shown, so the counts
                    match, but it is labelled rather than left as a blank row, and it offers no
                    adjustments: a seller cannot act on stock they cannot identify.
                  */
                  const identified = item.productName.trim().length > 0 && item.sku.trim().length > 0;

                  return (
                    <tr key={item.inventoryId}>
                      <td>
                        {identified ? (
                          <Link href={`/seller/products/${item.productId}`} style={{ color: "var(--text)" }}>
                            {item.productName}
                          </Link>
                        ) : (
                          <span style={{ color: "var(--text-muted)" }}>No longer in your catalogue</span>
                        )}
                        <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                          {identified ? item.variantName : "This listing was deleted or renamed"}
                        </span>
                      </td>
                      <td style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                        {identified ? item.sku : "—"}
                      </td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                        {item.availableQuantity}
                      </td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                        {item.reservedQuantity}
                      </td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", fontWeight: 600 }}>
                        {item.sellableQuantity}
                      </td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                        {item.lowStockThreshold}
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
                        {identified ? (
                          <div className="d-flex justify-content-end" style={{ gap: "0.35rem" }}>
                            <button
                              type="button"
                              className="btn btn-sm btn-primary"
                              onClick={() => setAdjusting(item)}
                              aria-label={`Adjust stock for ${item.productName}, ${item.variantName}`}
                            >
                              Adjust
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-outline-secondary"
                              onClick={() => setThresholding(item)}
                              aria-label={`Change the low-stock level for ${item.productName}, ${item.variantName}`}
                              title="Change the low-stock level"
                            >
                              <SlidersHorizontal size={14} aria-hidden />
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
                        ) : (
                          <span style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>Nothing to do</span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <Pagination page={inventory.data.page} totalPages={inventory.data.totalPages} query={filters} basePath={BASE_PATH} />
        </>
      )}

      {adjusting ? (
        <AdjustStockDialog
          item={adjusting}
          onClose={() => setAdjusting(null)}
          onDone={(item, delta) => {
            setAdjusting(null);
            push({
              tone: "success",
              title: delta > 0 ? "Stock added" : "Stock removed",
              body: `${item.sku} now has ${item.availableQuantity} on hand and ${item.sellableQuantity} sellable.`,
            });
          }}
        />
      ) : null}

      {thresholding ? (
        <ThresholdDialog
          item={thresholding}
          onClose={() => setThresholding(null)}
          onDone={item => {
            setThresholding(null);
            push({ tone: "success", title: "Low-stock level saved", body: `${item.sku} is flagged from now on.` });
          }}
        />
      ) : null}

      {historyFor ? <HistoryDialog item={historyFor} onClose={() => setHistoryFor(null)} /> : null}
    </div>
  );
}

function StockToolbar({
  search,
  filter,
  productId,
  onSearch,
  onFilter,
}: {
  search: string;
  filter: string;
  productId: string | undefined;
  onSearch: (value: string) => void;
  onFilter: (value: string) => void;
}) {
  const [draft, setDraft] = useState(search);
  const [showFilters, setShowFilters] = useState(false);

  return (
    <form
      role="search"
      aria-label="Search and filter stock"
      className="mp-card"
      style={{ padding: "var(--space-3) var(--space-4)" }}
      onSubmit={event => {
        event.preventDefault();
        onSearch(draft);
      }}
    >
      <div className="row g-2 align-items-end">
        <div className="col-12 col-md-5">
          <label htmlFor="stock-search" className="mp-metric-label">
            Search stock
          </label>
          <input
            id="stock-search"
            type="search"
            className="form-control form-control-sm"
            value={draft}
            onChange={event => setDraft(event.target.value)}
            placeholder="Product name or SKU"
            autoComplete="off"
          />
        </div>

        {/*
          A phone has no room for three filter buttons beside a search box, so the filter is a
          disclosure on small screens and always visible from md up. Same choices either way, so
          nothing is hidden behind the toggle that is not also in the URL.
        */}
        <div className="col-12 col-md-4 d-none d-md-block">
          <span className="mp-metric-label" id="stock-filter-label">
            Show
          </span>
          <FilterButtons filter={filter} onFilter={onFilter} labelledBy="stock-filter-label" />
        </div>

        <div className="col-12 col-md-3 d-flex align-items-end" style={{ gap: "0.5rem" }}>
          <button type="submit" className="btn btn-sm btn-primary flex-grow-1">
            Search
          </button>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary d-md-none"
            aria-expanded={showFilters}
            aria-controls="stock-filters-mobile"
            onClick={() => setShowFilters(open => !open)}
          >
            Filters
          </button>
        </div>
      </div>

      {showFilters ? (
        <div id="stock-filters-mobile" className="mt-2 d-md-none">
          <span className="visually-hidden" id="stock-filter-label-mobile">
            Show
          </span>
          <FilterButtons filter={filter} onFilter={onFilter} labelledBy="stock-filter-label-mobile" />
        </div>
      ) : null}

      {productId ? (
        <p className="mb-0 mt-2" style={{ fontSize: "var(--fs-sm)" }}>
          Scoped to one product.{" "}
          <Link href={BASE_PATH} className="mp-link">
            Show all stock
          </Link>
        </p>
      ) : null}
    </form>
  );
}

function FilterButtons({
  filter,
  onFilter,
  labelledBy,
}: {
  filter: string;
  onFilter: (value: string) => void;
  labelledBy: string;
}) {
  const options = [
    { value: "", label: "All" },
    { value: "low", label: "Low stock" },
    { value: "out", label: "Out of stock" },
  ];

  return (
    <div className="d-flex" style={{ gap: "0.35rem" }} role="group" aria-labelledby={labelledBy}>
      {options.map(option => (
        <button
          key={option.value || "all"}
          type="button"
          className={filter === option.value ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
          aria-pressed={filter === option.value}
          onClick={() => onFilter(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

/**
 * Move stock by an amount, and say why.
 *
 * The dialog states what is on hand first, because the number typed here is a change and not a
 * total: entering 12 means twelve more. The resulting figures shown on success are the API's own
 * answer rather than a figure calculated in the browser, so what the seller reads afterwards is
 * what was actually stored.
 */
function AdjustStockDialog({
  item,
  onClose,
  onDone,
}: {
  item: InventoryItem;
  onClose: () => void;
  onDone: (item: InventoryItem, delta: number) => void;
}) {
  const queryClient = useQueryClient();
  const [delta, setDelta] = useState("");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);

  const adjust = useMutation({
    mutationFn: () => sellerApi.adjustStock(item.productVariantId, { delta: Number(delta), reason: reason.trim() }),
    onSuccess: async updated => {
      await invalidateStock(queryClient, item.productVariantId);
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
      <p className="mb-3">
        {item.productName} · {item.variantName}
      </p>

      <dl className="row g-2 mb-3" style={{ fontSize: "var(--fs-sm)" }}>
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
          hint="Recorded against the movement. Required for every adjustment."
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

/** The level at or below which this variant is reported as low. */
function ThresholdDialog({
  item,
  onClose,
  onDone,
}: {
  item: InventoryItem;
  onClose: () => void;
  onDone: (item: InventoryItem) => void;
}) {
  const queryClient = useQueryClient();
  const [value, setValue] = useState(String(item.lowStockThreshold));
  const [error, setError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: () => sellerApi.setStockThreshold(item.productVariantId, Number(value)),
    onSuccess: async () => {
      await invalidateStock(queryClient, item.productVariantId);
      onDone({ ...item, lowStockThreshold: Number(value) });
    },
    onError: failure => setError(errorMessage(failure)),
  });

  const parsed = Number(value);
  const thresholdError = value.trim() === "" || !Number.isInteger(parsed) || parsed < 0 || parsed > 100000 ? "Enter zero or a whole number of units." : undefined;

  return (
    <ConfirmDialog
      show
      title={`Low-stock level for ${item.sku}`}
      confirmLabel="Save level"
      cancelLabel="Keep current level"
      tone="primary"
      busy={save.isPending}
      onCancel={onClose}
      onConfirm={() => {
        if (thresholdError) {
          return;
        }

        setError(null);
        save.mutate();
      }}
    >
      <p>
        {item.productName} · {item.variantName}
      </p>
      <p style={{ color: "var(--text-muted)" }}>
        It counts as low once sellable stock reaches this number. It is a warning, not a limit: nothing is prevented from
        selling.
      </p>

      <TextField
        label="Warn at or below"
        required
        type="number"
        inputMode="numeric"
        step="1"
        min="0"
        value={value}
        onChange={event => setValue(event.target.value)}
        error={thresholdError}
      />

      {error ? (
        <p role="alert" className="mp-alert mp-alert-danger mb-0">
          {error}
        </p>
      ) : null}
    </ConfirmDialog>
  );
}

/** The API's own ledger for one variant: what moved, why, and what was left afterwards. */
function HistoryDialog({ item, onClose }: { item: InventoryItem; onClose: () => void }) {
  const transactions = useQuery({
    queryKey: queryKeys.seller.inventoryTransactions(item.productVariantId),
    queryFn: () => sellerApi.inventoryTransactions(item.productVariantId, 50),
  });

  return (
    <ConfirmDialog
      show
      title={`Stock history for ${item.sku}`}
      confirmLabel="Close"
      cancelLabel="Close"
      tone="primary"
      onCancel={onClose}
      onConfirm={onClose}
    >
      <p className="mb-3">
        {item.productName} · {item.variantName} · {item.availableQuantity} on hand now
      </p>

      {transactions.isPending ? (
        <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius-sm)" }} />
      ) : transactions.isError ? (
        <ErrorState message="We could not load the history for this variant." onRetry={() => void transactions.refetch()} />
      ) : transactions.data.length === 0 ? (
        <p style={{ color: "var(--text-muted)", margin: 0 }}>
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

/**
 * Everything a stock change makes untrue.
 *
 * The variant's own ledger, every stock page, and the dashboard counts that read the same rows —
 * the public catalogue too, because a product page shows whether it can be bought. The product
 * detail read is included for the same reason: it shows the variant's available quantity.
 */
async function invalidateStock(queryClient: QueryClient, variantId: string) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.seller.inventoryLists() }),
    queryClient.invalidateQueries({ queryKey: queryKeys.seller.inventoryTransactions(variantId) }),
    queryClient.invalidateQueries({ queryKey: queryKeys.seller.productDetails() }),
    queryClient.invalidateQueries({ queryKey: queryKeys.seller.summary() }),
    queryClient.invalidateQueries({ queryKey: queryKeys.products.all }),
  ]);
}

function StockSkeleton() {
  return (
    <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} aria-hidden />
  );
}

function emptyTitle(filter: string | undefined, search: string | undefined, productId: string | undefined): string {
  if (productId) {
    return "This product has no stock records";
  }

  if (filter === "low") {
    return "Nothing is running low";
  }

  if (filter === "out") {
    return "Nothing is out of stock";
  }

  if (search) {
    return "No stock matches that search";
  }

  return "No stock records yet";
}

function emptyBody(filter: string | undefined, search: string | undefined, productId: string | undefined): string {
  if (productId) {
    return "A variant gets a stock record when it is created with an opening quantity. Add a variant, or adjust its stock, and it appears here.";
  }

  if (filter === "low") {
    return "Every variant is above its low-stock level.";
  }

  if (filter === "out") {
    return "Every variant still has stock that can be sold.";
  }

  if (search) {
    return "Try a different product name or SKU.";
  }

  return "Stock appears here once your products have variants. Each variant is tracked separately, because each is what an order is actually for.";
}