"use client";

/**
 * The seller's own discount codes.
 *
 * A merchant looks at this page to answer two questions: what is live, and what did I switch on last.
 * So the list leads with the code and what it takes off, states plainly whether it can be used at
 * checkout *right now*, and says so in words as well as colour — "starts tomorrow", "ended on the
 * 12th" — because a code whose window has closed looks identical to a live one in a list of names.
 *
 * Only the API's own fields appear. There is deliberately no redemptions column and no
 * "N products only": the API returns a usage count it never increments and a product-scope list it
 * never persists, and a screen that showed either would be reporting numbers nobody is recording.
 *
 * Search, status and page live in the URL, and stopping a code is confirmed first — it is
 * reversible, but it takes a code off sale at the counter, which is worth one question.
 */

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";

import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { Pagination } from "@/components/navigation/Pagination";
import { sellerCouponApi } from "@/features/seller/api/sellerCouponApi";
import { SellerCouponForm } from "@/features/seller/components/SellerCouponForm";
import { useSellerListUrl } from "@/features/seller/lib/listUrl";
import { errorMessage } from "@/lib/errors";
import { formatCurrency, formatDate, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { Coupon, CouponStatus, CreateCouponRequest, UpdateCouponRequest } from "@/types/coupon";

const BASE_PATH = "/seller/coupons";

/**
 * The statuses the API can actually hold.
 *
 * The enum has five members, but only Active and Paused are ever written: creation is hard-coded
 * to Active and the only other status a write can set is Paused. Offering Draft, Expired or
 * Exhausted as filters would produce an empty list every time somebody clicked them, so they are
 * left out and the reason is recorded here.
 */
const STATUSES: { id: CouponStatus; label: string }[] = [
  { id: "Active", label: "Active" },
  { id: "Paused", label: "Paused" },
];

const STATUS_IDS = STATUSES.map(option => option.id);

export function SellerCoupons() {
  const { status, search, page, navigate, filtered } = useSellerListUrl(BASE_PATH, { statuses: STATUS_IDS });
  const queryClient = useQueryClient();
  const { push } = useToast();

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<Coupon | null>(null);
  const [stopping, setStopping] = useState<Coupon | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<unknown>(null);

  const coupons = useQuery({
    queryKey: queryKeys.sellerCoupons.list({ page, status, search }),
    queryFn: () => sellerCouponApi.list({ page, pageSize: 20, status, search }),
  });

  const save = useMutation({
    mutationFn: (input: { id?: string; body: CouponFormBody }) =>
      input.id
        ? sellerCouponApi.update(input.id, toUpdateRequest(input.body))
        : sellerCouponApi.create(toCreateRequest(input.body)),
    onSuccess: async (saved, input) => {
      // Only the coupon reads change. The dashboard carries no coupon figures, so it is left alone
      // rather than refetched on the assumption that it might.
      await queryClient.invalidateQueries({ queryKey: queryKeys.sellerCoupons.all });
      setCreating(false);
      setEditing(null);
      setFormError(null);
      setServerError(null);
      push(
        input.id
          ? { tone: "success", title: `${saved.code} updated` }
          : { tone: "success", title: `${saved.code} created`, body: "It can be used at checkout straight away." },
      );
    },
    onError: error => {
      // The form keeps what was typed; the messages go onto the fields the API objected to.
      setServerError(error);
      setFormError(errorMessage(error));
    },
  });

  const stop = useMutation({
    mutationFn: (id: string) => sellerCouponApi.stop(id),
    onSuccess: async () => {
      const code = stopping?.code ?? "Code";
      setStopping(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.sellerCoupons.all });
      push({ tone: "success", title: `${code} stopped`, body: "It is no longer accepted at checkout. You can start it again from Edit." });
    },
    onError: error => {
      setStopping(null);
      setFormError(errorMessage(error));
    },
  });

  return (
    <div className="mp-stack">
      <CouponsToolbar search={search ?? ""} onSearch={value => navigate({ search: value.trim() || undefined })} onCreate={() => setCreating(true)} />

      <nav aria-label="Filter by status" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
        <button
          type="button"
          className={status === undefined ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
          aria-pressed={status === undefined}
          onClick={() => navigate({ status: undefined })}
        >
          All
        </button>
        {STATUSES.map(option => (
          <button
            key={option.id}
            type="button"
            className={status === option.id ? "btn btn-sm btn-primary" : "btn btn-sm btn-outline-secondary"}
            aria-pressed={status === option.id}
            onClick={() => navigate({ status: option.id })}
          >
            {option.label}
          </button>
        ))}
      </nav>

      {formError && !creating && !editing ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {formError}
        </p>
      ) : null}

      {creating ? (
        <SellerCouponForm
          busy={save.isPending}
          formError={formError}
          serverError={serverError}
          onCancel={() => {
            setCreating(false);
            setFormError(null);
            setServerError(null);
          }}
          onSubmit={body => save.mutate({ body })}
        />
      ) : null}

      {editing ? (
        <SellerCouponForm
          coupon={editing}
          busy={save.isPending}
          formError={formError}
          serverError={serverError}
          onCancel={() => {
            setEditing(null);
            setFormError(null);
            setServerError(null);
          }}
          onSubmit={body => save.mutate({ id: editing.id, body })}
        />
      ) : null}

      {coupons.isPending ? (
        <CouponsSkeleton />
      ) : coupons.isError || !coupons.data ? (
        <ErrorState message="We could not load your discount codes." onRetry={() => void coupons.refetch()} />
      ) : coupons.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "No codes match those filters" : "No discount codes yet"}
          body={
            filtered
              ? "Try another status, or clear the search to see every code you have."
              : "A code you create here is offered at checkout in your store. Nothing is published to other stores."
          }
          action={
            filtered ? (
              <button type="button" className="btn btn-sm btn-primary" onClick={() => navigate({ status: undefined, search: undefined })}>
                Show every code
              </button>
            ) : (
              <button type="button" className="btn btn-sm btn-primary" onClick={() => setCreating(true)}>
                Create a code
              </button>
            )
          }
        />
      ) : (
        <>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {formatNumber(coupons.data.totalCount)} {coupons.data.totalCount === 1 ? "code" : "codes"}
            {coupons.data.totalPages > 1 ? ` · page ${coupons.data.page} of ${coupons.data.totalPages}` : ""}
          </p>

          {/*
            A table on a desktop, cards on a phone. The information is identical either way — the
            card is the same row reflowed, not a reduced version of it, so nothing operational
            disappears on a small screen.
          */}
          <div className="d-none d-md-block">
            <div className="mp-table-wrap">
              <table className="mp-table">
                <caption className="visually-hidden">Your discount codes</caption>
                <thead>
                  <tr>
                    <th scope="col">Code</th>
                    <th scope="col">Discount</th>
                    <th scope="col">Conditions</th>
                    <th scope="col">Window</th>
                    <th scope="col">Status</th>
                    <th scope="col">
                      <span className="visually-hidden">Actions</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {coupons.data.items.map(coupon => (
                    <tr key={coupon.id}>
                      <td>
                        <span style={{ fontFamily: "var(--font-mono)", fontWeight: 700 }}>{coupon.code}</span>
                        {coupon.description ? (
                          <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                            {coupon.description}
                          </span>
                        ) : null}
                      </td>
                      <td style={{ whiteSpace: "nowrap" }}>{describe(coupon)}</td>
                      <td style={{ fontSize: "var(--fs-xs)", color: "var(--text-muted)" }}>{conditions(coupon)}</td>
                      <td style={{ fontSize: "var(--fs-xs)", color: "var(--text-muted)", whiteSpace: "nowrap" }}>
                        {formatDate(coupon.startsAt)} – {formatDate(coupon.endsAt)}
                      </td>
                      <td>
                        <StatusBadge tone={statusTone(coupon)}>{statusLabel(coupon)}</StatusBadge>
                      </td>
                      <td>
                        <div className="d-flex justify-content-end" style={{ gap: "0.35rem" }}>
                          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setEditing(coupon)} aria-label={`Edit ${coupon.code}`}>
                            Edit
                          </button>
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-danger"
                            onClick={() => setStopping(coupon)}
                            disabled={coupon.status === "Paused"}
                            aria-label={`Stop ${coupon.code}`}
                            title={coupon.status === "Paused" ? "Already stopped" : undefined}
                          >
                            Stop
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <ul className="list-unstyled mp-stack-sm d-md-none mb-0">
            {coupons.data.items.map(coupon => (
              <li key={coupon.id} className="mp-card" style={{ padding: "var(--space-4)" }}>
                <div className="mp-spread">
                  <div style={{ minWidth: 0 }}>
                    <span style={{ fontFamily: "var(--font-mono)", fontWeight: 700, fontSize: "var(--fs-h3)" }}>{coupon.code}</span>
                    <p style={{ margin: 0, fontSize: "var(--fs-sm)" }}>{describe(coupon)}</p>
                  </div>
                  <StatusBadge tone={statusTone(coupon)}>{statusLabel(coupon)}</StatusBadge>
                </div>

                <p style={{ margin: "var(--space-3) 0 0", fontSize: "var(--fs-xs)", color: "var(--text-muted)" }}>{conditions(coupon)}</p>
                <p style={{ margin: 0, fontSize: "var(--fs-xs)", color: "var(--text-muted)" }}>
                  {formatDate(coupon.startsAt)} – {formatDate(coupon.endsAt)}
                </p>
                {coupon.description ? (
                  <p style={{ margin: "var(--space-2) 0 0", fontSize: "var(--fs-xs)", color: "var(--text-subtle)" }}>{coupon.description}</p>
                ) : null}

                <div className="d-flex mt-3" style={{ gap: "0.5rem" }}>
                  <button type="button" className="btn btn-sm btn-outline-secondary flex-grow-1" onClick={() => setEditing(coupon)} aria-label={`Edit ${coupon.code}`}>
                    Edit
                  </button>
                  <button
                    type="button"
                    className="btn btn-sm btn-outline-danger flex-grow-1"
                    onClick={() => setStopping(coupon)}
                    disabled={coupon.status === "Paused"}
                    aria-label={`Stop ${coupon.code}`}
                  >
                    {coupon.status === "Paused" ? "Stopped" : "Stop"}
                  </button>
                </div>
              </li>
            ))}
          </ul>

          <Pagination page={coupons.data.page} totalPages={coupons.data.totalPages} query={{ status, search }} basePath={BASE_PATH} />
        </>
      )}

      {stopping ? (
        <ConfirmDialog
          show
          title={`Stop ${stopping.code}?`}
          confirmLabel={`Stop ${stopping.code}`}
          cancelLabel="Leave it running"
          busy={stop.isPending}
          onCancel={() => setStopping(null)}
          onConfirm={() => stop.mutate(stopping.id)}
        >
          <p className="mb-3">{describe(stopping)}</p>
          <p className="mb-0">
            Nobody can use <strong>{stopping.code}</strong> at checkout from the moment you confirm. The code and its
            settings are kept, and you can start it again from Edit — it is not deleted, so anybody who already has it
            will be told the code has stopped rather than that it never existed.
          </p>
        </ConfirmDialog>
      ) : null}
    </div>
  );
}

/** The form's single shape, sent to whichever endpoint is being called. */
type CouponFormBody = CreateCouponRequest & Pick<UpdateCouponRequest, "isActive">;

/**
 * The two request shapes are not the same shape, and the difference matters: create carries the code
 * and the kind of discount and has no active flag, while update carries the flag and neither of the
 * other two — the API will not change them. Building each from the form's values means neither
 * request can contain a property its endpoint does not accept.
 */
function toCreateRequest(body: CouponFormBody): CreateCouponRequest {
  return {
    code: body.code,
    description: body.description,
    discountType: body.discountType,
    discountValue: body.discountValue,
    minimumOrderAmount: body.minimumOrderAmount,
    maximumDiscountAmount: body.maximumDiscountAmount,
    usageLimit: body.usageLimit,
    perUserLimit: body.perUserLimit,
    startsAt: body.startsAt,
    endsAt: body.endsAt,
    productIds: null,
  };
}

function toUpdateRequest(body: CouponFormBody): UpdateCouponRequest {
  return {
    description: body.description,
    discountValue: body.discountValue,
    minimumOrderAmount: body.minimumOrderAmount,
    maximumDiscountAmount: body.maximumDiscountAmount,
    usageLimit: body.usageLimit,
    perUserLimit: body.perUserLimit,
    startsAt: body.startsAt,
    endsAt: body.endsAt,
    isActive: body.isActive,
    productIds: null,
  };
}

/** Search by code, and the one action a merchant comes here to perform. */
function CouponsToolbar({ search, onSearch, onCreate }: { search: string; onSearch: (value: string) => void; onCreate: () => void }) {
  const [draft, setDraft] = useState(search);

  return (
    <form
      role="search"
      aria-label="Search your discount codes"
      className="mp-card"
      style={{ padding: "var(--space-3) var(--space-4)" }}
      onSubmit={event => {
        event.preventDefault();
        onSearch(draft);
      }}
    >
      <div className="row g-2 align-items-end">
        <div className="col-12 col-sm-7 col-md-8">
          <label htmlFor="coupon-search" className="mp-metric-label">
            Search codes
          </label>
          <input
            id="coupon-search"
            type="search"
            className="form-control form-control-sm"
            value={draft}
            onChange={event => setDraft(event.target.value)}
            placeholder="A code, e.g. SPRING15"
            autoComplete="off"
          />
        </div>
        <div className="col-12 col-sm-5 col-md-4">
          <button type="button" className="btn btn-sm btn-primary w-100" onClick={onCreate}>
            <Plus size={14} aria-hidden className="me-1" />
            New code
          </button>
        </div>
      </div>
    </form>
  );
}

/** What a shopper actually pays less, in words. */
function describe(coupon: Coupon): string {
  if (coupon.discountType === "Percentage") {
    return coupon.maximumDiscountAmount
      ? `${coupon.discountValue}% off, up to ${formatCurrency(coupon.maximumDiscountAmount)}`
      : `${coupon.discountValue}% off`;
  }

  return `${formatCurrency(coupon.discountValue)} off`;
}

/**
 * The rules that decide whether the code applies, all of them configured by the seller and all of
 * them real. What is deliberately absent is a redemptions figure: the API returns a usage count that
 * nothing increments, so a "0 used" column would be reporting a number nobody records.
 */
function conditions(coupon: Coupon): string {
  const parts: string[] = [];

  if (coupon.minimumOrderAmount) {
    parts.push(`over ${formatCurrency(coupon.minimumOrderAmount)}`);
  }

  parts.push(`${coupon.perUserLimit} ${coupon.perUserLimit === 1 ? "use" : "uses"} per person`);
  parts.push(coupon.usageLimit ? `up to ${coupon.usageLimit} total` : "no total limit");

  return parts.join(" · ");
}

/**
 * Two facts, not one: the stored status and whether the code can be used at this moment. `isActiveNow`
 * is the API's own derivation — active, and inside its window — so a code that is Active but not yet
 * running does not look like one that is.
 */
function statusLabel(coupon: Coupon): string {
  if (coupon.isActiveNow) {
    return "Live now";
  }

  if (coupon.status === "Paused") {
    return "Paused";
  }

  if (new Date(coupon.startsAt).getTime() > Date.now()) {
    return `Starts ${formatDate(coupon.startsAt)}`;
  }

  return `Ended ${formatDate(coupon.endsAt)}`;
}

function statusTone(coupon: Coupon): "success" | "warning" | "info" {
  if (coupon.isActiveNow) {
    return "success";
  }

  return coupon.status === "Paused" ? "warning" : "info";
}

function CouponsSkeleton() {
  return (
    <div className="mp-stack-sm" aria-hidden>
      {[0, 1, 2].map(index => (
        <div key={index} className="mp-card" style={{ padding: "var(--space-4)" }}>
          <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
            <div className="mp-skeleton" style={{ height: "1.25rem", width: "8rem" }} />
            <div className="mp-skeleton" style={{ height: "1.1rem", width: "6rem" }} />
          </div>
          <div className="mp-skeleton" style={{ height: "0.9rem", width: "70%" }} />
        </div>
      ))}
    </div>
  );
}