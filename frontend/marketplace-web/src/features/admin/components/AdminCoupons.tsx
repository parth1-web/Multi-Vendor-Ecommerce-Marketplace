/**
 * Discount codes, and making one.
 *
 * A marketplace with no way to create a promotion has one that exists only because the seed put
 * it there, so this is the screen an operator opens to run a sale. It leads with the code itself
 * and what it takes off, because that is what somebody deciding whether to use it needs, and the
 * rules that govern it — when it starts, when it ends, how many times, per person — are stated
 * rather than buried in a settings page.
 *
 * The maths belongs to the server. A percentage of a basket, a cap on what it takes off and the
 * rules for when it does not apply are all decided in one place, so this form collects the
 * numbers and shows back what the server made of them.
 */

"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { TextAreaField, TextField } from "@/components/forms/FormField";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { cx, formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { Coupon, CouponDiscountType, CouponStatus, CreateCouponRequest, UpdateCouponRequest } from "@/types/coupon";

const STATUSES: CouponStatus[] = ["Draft", "Active", "Paused", "Expired", "Exhausted"];

/** What a shopper is charged, in words, for each kind of discount. */
function describe(coupon: Coupon): string {
  const value = coupon.discountType === "Percentage"
    ? `${coupon.discountValue}% off`
    : `${formatCurrency(coupon.discountValue)} off`;

  if (coupon.discountType === "Percentage" && coupon.maximumDiscountAmount) {
    return `${value}, up to ${formatCurrency(coupon.maximumDiscountAmount)}`;
  }

  return value;
}

export function AdminCoupons() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<CouponStatus | "">("");
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<Coupon | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const coupons = useQuery({
    queryKey: queryKeys.admin.coupons({ page, status, term }),
    queryFn: () => adminApi.coupons({ page, pageSize: 20, status: status || undefined, search: term || undefined }),
  });

  const afterChange = async () => {
    setActionError(null);
    setCreating(false);
    setEditing(null);
    // A discount code changes the coupon list. It moves no sales figure until a shopper uses
    // one, so the reports and the overview are deliberately left alone.
    await queryClient.invalidateQueries({ queryKey: queryKeys.admin.coupons({})[0] });
  };

  const save = useMutation({
    mutationFn: (input: { id?: string; body: CreateCouponRequest & { isActive?: boolean } }) =>
      input.id
        ? adminApi.updateCoupon(input.id, input.body as UpdateCouponRequest)
        : adminApi.createCoupon(input.body as CreateCouponRequest),
    onSuccess: afterChange,
    onError: error => setActionError(errorMessage(error)),
  });

  const stop = useMutation({
    mutationFn: (id: string) => adminApi.stopCoupon(id),
    onSuccess: afterChange,
    onError: error => setActionError(errorMessage(error)),
  });

  return (
    <div className="mp-stack">
      <div className="d-flex flex-wrap justify-content-between align-items-end" style={{ gap: "var(--space-3)" }}>
        <nav aria-label="Filter by status" className="d-flex flex-wrap" style={{ gap: "0.4rem" }}>
          <button
            type="button"
            className={cx("btn btn-sm", status === "" ? "btn-primary" : "btn-outline-secondary")}
            onClick={() => {
              setStatus("");
              setPage(1);
            }}
          >
            All
          </button>
          {STATUSES.map(value => (
            <button
              key={value}
              type="button"
              className={cx("btn btn-sm", status === value ? "btn-primary" : "btn-outline-secondary")}
              onClick={() => {
                setStatus(value);
                setPage(1);
              }}
            >
              {value}
            </button>
          ))}
        </nav>

        <div className="d-flex" style={{ gap: "0.5rem" }}>
          <form
            className="d-flex"
            style={{ gap: "0.5rem" }}
            onSubmit={event => {
              event.preventDefault();
              setTerm(search.trim().toUpperCase());
              setPage(1);
            }}
          >
            <label htmlFor="coupon-search" className="visually-hidden">
              Search by code
            </label>
            <input
              id="coupon-search"
              className="mp-input"
              value={search}
              placeholder="Code"
              onChange={event => setSearch(event.target.value)}
            />
            <button type="submit" className="btn btn-sm btn-outline-secondary">
              Search
            </button>
          </form>

          <button type="button" className="btn btn-sm btn-primary" onClick={() => setCreating(value => !value)} aria-expanded={creating}>
            {creating ? "Cancel" : "New code"}
          </button>
        </div>
      </div>

      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {creating ? (
        <CouponForm
          busy={save.isPending}
          onSubmit={body => save.mutate({ body })}
        />
      ) : null}

      {editing ? (
        <CouponForm
          coupon={editing}
          busy={save.isPending}
          onSubmit={body => save.mutate({ id: editing.id, body })}
        />
      ) : null}

      {coupons.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />
      ) : coupons.isError || !coupons.data ? (
        <ErrorState message="We could not load the discount codes." />
      ) : coupons.data.items.length === 0 ? (
        <EmptyState
          title={term || status ? "Nothing matches" : "No discount codes yet"}
          body={term || status ? "Try another filter." : "Create one and it can be used at checkout straight away."}
        />
      ) : (
        <div className="mp-stack-sm">
          {coupons.data.items.map(coupon => (
            <article key={coupon.id} className="mp-card" style={{ padding: "var(--space-4)" }}>
              <div className="mp-spread">
                <div>
                  <span style={{ fontFamily: "var(--font-mono)", fontWeight: 700, fontSize: "var(--fs-h3)" }}>{coupon.code}</span>
                  <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                    {describe(coupon)}
                    {coupon.scope === "Seller" && coupon.sellerName ? ` · ${coupon.sellerName}'s store only` : " · the whole marketplace"}
                  </p>
                  {coupon.description ? (
                    <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{coupon.description}</p>
                  ) : null}
                </div>

                <div className="d-flex align-items-center" style={{ gap: "var(--space-3)" }}>
                  <StatusBadge tone={coupon.isActiveNow ? "success" : coupon.status === "Draft" ? "info" : "warning"}>
                    {coupon.isActiveNow ? "Live" : coupon.status}
                  </StatusBadge>

                  <div className="d-flex" style={{ gap: "0.5rem" }}>
                    <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setEditing(coupon)}>
                      Edit
                    </button>
                    {/* Not "Delete": stopping a code is what the server does, the row stays
                        because its usage is part of the record, and a stopped code can be started
                        again from Edit. Calling that a delete would be a promise it does not keep. */}
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-secondary"
                      style={{ color: "var(--danger)" }}
                      disabled={stop.isPending || !coupon.isActiveNow}
                      title={coupon.isActiveNow ? "Stops the code applying. It can be started again from Edit." : "Already stopped"}
                      onClick={() => stop.mutate(coupon.id)}
                    >
                      Stop
                    </button>
                  </div>
                </div>
              </div>

              <div className="d-flex flex-wrap mt-3" style={{ gap: "var(--space-4)", fontSize: "var(--fs-xs)", color: "var(--text-muted)" }}>
                <span>
                  {formatDate(coupon.startsAt)} to {formatDate(coupon.endsAt)}
                </span>
                <span>
                  {coupon.usageCount}
                  {coupon.usageLimit ? ` of ${coupon.usageLimit}` : ""} used
                </span>
                <span>{coupon.perUserLimit} per person</span>
                {coupon.minimumOrderAmount ? <span>over {formatCurrency(coupon.minimumOrderAmount)}</span> : null}
                {coupon.productIds.length > 0 ? <span>{coupon.productIds.length} products only</span> : null}
              </div>
            </article>
          ))}
        </div>
      )}

      {coupons.data && coupons.data.totalPages > 1 ? (
        <nav aria-label="Coupon pages" className="d-flex justify-content-between align-items-center mt-3">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Previous
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {coupons.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(coupons.data!.totalPages, p + 1))}
            disabled={page >= coupons.data.totalPages}
          >
            Next
          </button>
        </nav>
      ) : null}
    </div>
  );
}

function CouponForm({
  coupon,
  busy,
  onSubmit,
}: {
  coupon?: Coupon;
  busy: boolean;
  onSubmit: (body: CreateCouponRequest & { isActive?: boolean }) => void;
}) {
  const [code, setCode] = useState(coupon?.code ?? "");
  const [description, setDescription] = useState(coupon?.description ?? "");
  const [discountType, setDiscountType] = useState<CouponDiscountType>(coupon?.discountType ?? "Percentage");
  const [discountValue, setDiscountValue] = useState(String(coupon?.discountValue ?? 10));
  const [minimum, setMinimum] = useState(coupon?.minimumOrderAmount === null || coupon?.minimumOrderAmount === undefined ? "" : String(coupon.minimumOrderAmount));
  const [maximum, setMaximum] = useState(coupon?.maximumDiscountAmount === null || coupon?.maximumDiscountAmount === undefined ? "" : String(coupon.maximumDiscountAmount));
  const [usageLimit, setUsageLimit] = useState(coupon?.usageLimit === null || coupon?.usageLimit === undefined ? "" : String(coupon.usageLimit));
  const [perUserLimit, setPerUserLimit] = useState(String(coupon?.perUserLimit ?? 1));
  const [startsAt, setStartsAt] = useState(toDateInput(coupon?.startsAt ?? new Date().toISOString()));
  const [endsAt, setEndsAt] = useState(toDateInput(coupon?.endsAt ?? inThirtyDays()));
  const [formError, setFormError] = useState<string | null>(null);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);

    const value = Number(discountValue);

    if (coupon ? false : code.trim().length < 3) {
      setFormError("Give the code at least three characters. People type it from a screen or a message.");
      return;
    }

    if (!Number.isFinite(value) || value <= 0) {
      setFormError("Enter how much it takes off.");
      return;
    }

    if (discountType === "Percentage" && value > 100) {
      setFormError("A percentage cannot be more than 100.");
      return;
    }

    if (endsAt <= startsAt) {
      setFormError("The end has to be after the start.");
      return;
    }

    const perUser = Number(perUserLimit);
    if (!Number.isFinite(perUser) || perUser < 1) {
      setFormError("Allow at least one use per person, or nobody can use the code at all.");
      return;
    }

    onSubmit({
      code: code.trim().toUpperCase(),
      description: description.trim() || null,
      discountType,
      discountValue: value,
      minimumOrderAmount: minimum.trim() === "" ? null : Number(minimum),
      maximumDiscountAmount: maximum.trim() === "" ? null : Number(maximum),
      usageLimit: usageLimit.trim() === "" ? null : Number(usageLimit),
      perUserLimit: perUser,
      startsAt: new Date(startsAt).toISOString(),
      endsAt: new Date(endsAt).toISOString(),
      productIds: null,
      isActive: coupon ? coupon.isActiveNow : true,
    });
  }

  const isPercentage = discountType === "Percentage";

  return (
    <form onSubmit={submit} className="mp-card mp-stack" style={{ padding: "var(--space-4)" }} noValidate>
      <h2 className="mp-section-title" style={{ margin: 0, fontSize: "var(--fs-h3)" }}>
        {coupon ? `Edit ${coupon.code}` : "A new discount code"}
      </h2>

      {formError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {formError}
        </p>
      ) : null}

      <div className="row g-2">
        <div className="col-12 col-md-4">
          <TextField
            label="Code"
            required
            value={code}
            disabled={Boolean(coupon)}
            hint={coupon ? "A code cannot be changed once people know it." : "People type this exactly as it is here."}
            onChange={event => setCode(event.target.value)}
          />
        </div>

        <div className="col-6 col-md-3">
          <label htmlFor="coupon-type" className="mp-metric-label">
            Kind of discount
          </label>
          <select
            id="coupon-type"
            className="mp-input"
            value={discountType}
            disabled={Boolean(coupon)}
            onChange={event => setDiscountType(event.target.value as CouponDiscountType)}
          >
            <option value="Percentage">Percentage off</option>
            <option value="FixedAmount">Money off</option>
          </select>
        </div>

        <div className="col-6 col-md-3">
          <TextField
            label={isPercentage ? "Percentage" : "Amount"}
            required
            type="number"
            step="0.01"
            min="0"
            max={isPercentage ? "100" : undefined}
            value={discountValue}
            onChange={event => setDiscountValue(event.target.value)}
          />
        </div>

        {isPercentage ? (
          <div className="col-6 col-md-2">
            <TextField
              label="Up to"
              type="number"
              step="0.01"
              min="0"
              value={maximum}
              hint="Optional"
              placeholder="No cap"
              onChange={event => setMaximum(event.target.value)}
            />
          </div>
        ) : null}
      </div>

      <div className="row g-2">
        <div className="col-6 col-md-3">
          <TextField
            label="Over"
            type="number"
            step="0.01"
            min="0"
            value={minimum}
            hint="Minimum basket"
            placeholder="Any"
            onChange={event => setMinimum(event.target.value)}
          />
        </div>

        <div className="col-6 col-md-3">
          <TextField
            label="Total uses"
            type="number"
            min="1"
            value={usageLimit}
            hint="Blank for no limit"
            placeholder="No limit"
            onChange={event => setUsageLimit(event.target.value)}
          />
        </div>

        <div className="col-6 col-md-3">
          <TextField
            label="Per person"
            required
            type="number"
            min="1"
            value={perUserLimit}
            onChange={event => setPerUserLimit(event.target.value)}
          />
        </div>
      </div>

      <div className="row g-2">
        <div className="col-6 col-md-4">
          <TextField label="Starts" required type="date" value={startsAt} onChange={event => setStartsAt(event.target.value)} />
        </div>
        <div className="col-6 col-md-4">
          <TextField label="Ends" required type="date" value={endsAt} onChange={event => setEndsAt(event.target.value)} />
        </div>
      </div>

      <TextAreaField
        label="What it is for"
        rows={2}
        value={description}
        placeholder="Shown to nobody, but it is what tells you next month why this code exists."
        onChange={event => setDescription(event.target.value)}
      />

      <div>
        <button type="submit" className="btn btn-sm btn-primary" disabled={busy}>
          {busy ? "Saving…" : coupon ? "Save changes" : "Create code"}
        </button>
      </div>
    </form>
  );
}

function toDateInput(iso: string): string {
  const date = new Date(iso);

  return Number.isNaN(date.getTime()) ? "" : date.toISOString().slice(0, 10);
}

function inThirtyDays(): string {
  const date = new Date();
  date.setDate(date.getDate() + 30);

  return date.toISOString();
}
