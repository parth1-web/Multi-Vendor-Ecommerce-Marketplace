"use client";

/**
 * A discount code the seller owns, created or edited.
 *
 * The form is exactly the API's two request shapes and nothing more. `CreateCouponRequest` accepts a
 * code, a kind of discount, a value, an optional minimum basket, an optional cap, an optional
 * total-use limit, a per-person limit and a window; `UpdateCouponRequest` is the same list without
 * the code and the kind, plus an active flag. The two fields the API will not change are shown
 * read-only rather than hidden, because a seller editing a code needs to see which one it is.
 *
 * Validation mirrors the server's own rules so a refusal is not a surprise: 3–32 letters, digits,
 * dashes or underscores; a percentage between 0.01 and 100; a fixed amount of at least 0.50; an end
 * after the start and in the future. Where the server is the only authority — whether the code is
 * already taken, whether the store may hold one at all — its message is put on the field rather than
 * replaced with something vaguer.
 */

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";

import { CheckField, SelectField, TextAreaField, TextField } from "@/components/forms/FormField";
import { fieldErrors } from "@/lib/errors";
import { formatCurrency } from "@/lib/format";
import type { Coupon, CouponDiscountType, CreateCouponRequest, UpdateCouponRequest } from "@/types/coupon";

/** This form's field names, in the order the API names them. */
const COUPON_FIELDS = [
  "code",
  "description",
  "discountType",
  "discountValue",
  "minimumOrderAmount",
  "maximumDiscountAmount",
  "usageLimit",
  "perUserLimit",
  "startsAt",
  "endsAt",
  "isActive",
] as const;

const schema = z
  .object({
    code: z
      .string()
      .trim()
      .regex(/^[A-Za-z0-9_-]{3,32}$/, "Use 3-32 letters, digits, dashes or underscores."),
    description: z.string().trim().max(500),
    discountType: z.enum(["Percentage", "FixedAmount"]),
    discountValue: z.coerce.number({ invalid_type_error: "Enter how much it takes off." }).positive("Enter how much it takes off."),
    minimumOrderAmount: z.string().trim(),
    maximumDiscountAmount: z.string().trim(),
    usageLimit: z.string().trim(),
    perUserLimit: z.coerce
      .number({ invalid_type_error: "Allow at least one use per person." })
      .int("Use a whole number.")
      .min(1, "Allow at least one use per person.")
      .max(1000, "The API accepts up to 1000."),
    startsAt: z.string().min(1, "Choose when the code starts."),
    endsAt: z.string().min(1, "Choose when the code ends."),
    isActive: z.boolean(),
  })
  .superRefine((values, ctx) => {
    if (values.discountType === "Percentage") {
      if (values.discountValue > 100) {
        ctx.addIssue({ code: z.ZodIssueCode.custom, path: ["discountValue"], message: "A percentage cannot be more than 100." });
      } else if (values.discountValue < 0.01) {
        ctx.addIssue({ code: z.ZodIssueCode.custom, path: ["discountValue"], message: "A percentage must be at least 0.01." });
      }
    } else if (values.discountValue < 0.5) {
      // The API rounds a fixed discount to whole amounts and refuses the ones that round to zero.
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["discountValue"],
        message: "A fixed discount is rounded to whole amounts, so it has to be at least 0.50.",
      });
    }

    // A blank field means "not set", and the API takes null for these; anything present must be usable.
    for (const [field, label] of [
      ["minimumOrderAmount", "The minimum basket"],
      ["maximumDiscountAmount", "The cap"],
      ["usageLimit", "The total-use limit"],
    ] as const) {
      const raw = values[field];

      if (raw !== "" && (!Number.isFinite(Number(raw)) || Number(raw) <= 0)) {
        ctx.addIssue({ code: z.ZodIssueCode.custom, path: [field], message: `${label} has to be above zero.` });
      }
    }

    if (values.startsAt && values.endsAt) {
      const start = new Date(values.startsAt).getTime();
      const end = new Date(values.endsAt).getTime();

      if (Number.isFinite(start) && Number.isFinite(end) && end <= start) {
        ctx.addIssue({ code: z.ZodIssueCode.custom, path: ["endsAt"], message: "The end has to be after the start." });
      }

      // The API refuses any save whose end date has passed, so editing an expired code means giving
      // it a new window. Saying so here beats a 400 the seller has to decode.
      if (Number.isFinite(end) && end <= Date.now()) {
        ctx.addIssue({ code: z.ZodIssueCode.custom, path: ["endsAt"], message: "The end has to be in the future." });
      }
    }
  });

export type SellerCouponValues = z.infer<typeof schema>;

/** What the parent receives: the create shape, with the flag the update shape also carries. */
type CouponFormBody = CreateCouponRequest & Pick<UpdateCouponRequest, "isActive">;

interface SellerCouponFormProps {
  /** The code being edited. Absent means a new code. */
  coupon?: Coupon;
  busy: boolean;
  /** A banner message: the failure as a whole, not attributable to one field. */
  formError: string | null;
  /** The last failure, so the API's own per-field messages can be placed on the fields. */
  serverError?: unknown;
  onSubmit: (body: CouponFormBody) => void;
  onCancel: () => void;
}

export function SellerCouponForm({ coupon, busy, formError, serverError, onSubmit, onCancel }: SellerCouponFormProps) {
  const isEdit = Boolean(coupon);

  const {
    register,
    handleSubmit,
    setError,
    clearErrors,
    formState: { errors, isSubmitting },
  } = useForm<SellerCouponValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: coupon?.code ?? "",
      description: coupon?.description ?? "",
      discountType: coupon?.discountType ?? "Percentage",
      discountValue: coupon?.discountValue ?? 10,
      minimumOrderAmount: blank(coupon?.minimumOrderAmount),
      maximumDiscountAmount: blank(coupon?.maximumDiscountAmount),
      usageLimit: blank(coupon?.usageLimit),
      perUserLimit: coupon?.perUserLimit ?? 1,
      startsAt: toLocalInput(coupon?.startsAt ?? new Date().toISOString()),
      endsAt: toLocalInput(coupon?.endsAt ?? inThirtyDays()),
      // A code being edited is live only if the API says it is right now; a new one is created
      // active, which is the only state create can produce.
      isActive: coupon ? coupon.isActiveNow : true,
    },
  });

  /*
   * The kind of discount is mirrored into state rather than read back with `watch()`: the compiler
   * cannot memoise that call, so it opts this whole component out of memoisation. A select is also
   * the one control here whose value changes what else the form offers, so it is the one value that
   * genuinely has to be read during render. The registered inputs themselves stay uncontrolled.
   */
  const [discountType, setDiscountType] = useState(coupon?.discountType ?? "Percentage");
  const isPercentage = discountType === "Percentage";

  /*
   * The cap only means anything for a percentage — a fixed amount is already fixed — so the field
   * disappears for one, and its message goes with it rather than lurking on a hidden input.
   */
  useEffect(() => {
    if (!isPercentage) {
      clearErrors("maximumDiscountAmount");
    }
  }, [isPercentage, clearErrors]);

  /*
   * The API names fields in its own casing (`discountValue`) while a form registers them in
   * camelCase, and `lib/errors` lower-cases every key. Comparing without regard to case is what
   * keeps "A percentage discount must be between 0.01 and 100." on the box the seller is looking
   * at rather than in a banner above the form.
   */
  useEffect(() => {
    if (!serverError) {
      return;
    }

    for (const [key, message] of Object.entries(fieldErrors(serverError))) {
      const field = COUPON_FIELDS.find(name => name.toLowerCase() === key);

      if (field) {
        setError(field, { message });
      }
    }
  }, [serverError, setError]);

  function submit(values: SellerCouponValues) {
    const send = (raw: string) => (raw.trim() === "" ? null : Number(raw));

    onSubmit({
      // Sent exactly as typed: the API uppercases and trims it itself, and a code the seller did
      // not expect to be rewritten is a code they will not recognise.
      code: values.code.trim(),
      description: values.description.trim() || null,
      discountType: values.discountType,
      discountValue: Number(values.discountValue),
      minimumOrderAmount: send(values.minimumOrderAmount),
      maximumDiscountAmount: isPercentage ? send(values.maximumDiscountAmount) : null,
      usageLimit: send(values.usageLimit),
      perUserLimit: Number(values.perUserLimit),
      startsAt: new Date(values.startsAt).toISOString(),
      endsAt: new Date(values.endsAt).toISOString(),
      // The API accepts no product scoping that it persists, so it is never sent.
      productIds: null,
      isActive: values.isActive,
    });
  }

  return (
    <form onSubmit={handleSubmit(submit)} noValidate className="mp-card mp-stack" style={{ padding: "var(--space-4)" }} aria-labelledby="coupon-form">
      <div className="mp-spread">
        <h2 className="mp-section-title" id="coupon-form" style={{ margin: 0, fontSize: "var(--fs-h3)" }}>
          {isEdit ? `Edit ${coupon!.code}` : "A new discount code"}
        </h2>
        {isEdit ? (
          <p style={{ margin: 0, fontSize: "var(--fs-xs)", color: "var(--text-subtle)" }}>
            {coupon!.discountType === "Percentage" ? `${coupon!.discountValue}% off` : `${formatCurrency(coupon!.discountValue)} off`}
          </p>
        ) : null}
      </div>

      {formError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {formError}
        </p>
      ) : null}

      <div className="row g-2">
        <div className="col-12 col-md-5">
          <TextField
            label="Code"
            required
            disabled={isEdit}
            maxLength={32}
            autoComplete="off"
            spellCheck={false}
            placeholder="SPRING15"
            hint={
              isEdit
                ? "A code cannot be changed once shoppers know it."
                : "3–32 letters, digits, dashes or underscores. Stored in capitals, so SPRING15 and spring15 are the same code."
            }
            error={errors.code?.message}
            {...register("code")}
          />
        </div>

        <div className="col-6 col-md-3">
          <SelectField
            label="Kind of discount"
            required
            disabled={isEdit}
            hint={isEdit ? "Fixed when the code is created." : undefined}
            error={errors.discountType?.message}
            {...register("discountType", {
              // Mirrored into state so the form can show the fields that apply to this kind.
              onChange: event => setDiscountType(event.target.value as CouponDiscountType),
            })}
          >
            <option value="Percentage">Percentage off</option>
            <option value="FixedAmount">Money off</option>
          </SelectField>
        </div>

        <div className="col-6 col-md-4">
          <TextField
            label={isPercentage ? "Percentage" : "Money off"}
            required
            type="number"
            inputMode="decimal"
            step="0.01"
            min="0"
            max={isPercentage ? "100" : undefined}
            hint={isPercentage ? "Between 0.01 and 100." : "Rounded to whole amounts by the API."}
            error={errors.discountValue?.message}
            {...register("discountValue")}
          />
        </div>
      </div>

      <div className="row g-2">
        <div className="col-6 col-md-3">
          <TextField
            label="Minimum basket"
            type="number"
            inputMode="decimal"
            step="0.01"
            min="0"
            placeholder="Any"
            error={errors.minimumOrderAmount?.message}
            {...register("minimumOrderAmount")}
          />
        </div>

        {isPercentage ? (
          <div className="col-6 col-md-3">
            <TextField
              label="Cap the discount at"
              type="number"
              inputMode="decimal"
              step="0.01"
              min="0"
              placeholder="No cap"
              hint="Optional. Stops a large basket taking more than this off."
              error={errors.maximumDiscountAmount?.message}
              {...register("maximumDiscountAmount")}
            />
          </div>
        ) : null}

        <div className="col-6 col-md-3">
          <TextField
            label="Total uses"
            type="number"
            inputMode="numeric"
            step="1"
            min="1"
            placeholder="No limit"
            hint="Optional. Blank means the code is not limited."
            error={errors.usageLimit?.message}
            {...register("usageLimit")}
          />
        </div>

        <div className="col-6 col-md-3">
          <TextField
            label="Per person"
            required
            type="number"
            inputMode="numeric"
            step="1"
            min="1"
            max="1000"
            hint="How many times one customer may use it."
            error={errors.perUserLimit?.message}
            {...register("perUserLimit")}
          />
        </div>
      </div>

      <div className="row g-2">
        <div className="col-6 col-md-4">
          <TextField
            label="Starts"
            required
            type="datetime-local"
            hint="The code is refused before this moment."
            error={errors.startsAt?.message}
            {...register("startsAt")}
          />
        </div>
        <div className="col-6 col-md-4">
          <TextField
            label="Ends"
            required
            type="datetime-local"
            hint="Must be in the future to be saved."
            error={errors.endsAt?.message}
            {...register("endsAt")}
          />
        </div>
      </div>

      <TextAreaField
        label="What it is for"
        rows={2}
        maxLength={500}
        placeholder="Shown to nobody. It is what tells you next month why this code exists."
        error={errors.description?.message}
        {...register("description")}
      />

      {isEdit ? (
        <CheckField
          label="This code can be used at checkout"
          hint="Turn this off rather than stopping it, if you would rather keep the settings as they are."
          {...register("isActive")}
        />
      ) : null}

      <div className="d-flex flex-wrap" style={{ gap: "0.5rem" }}>
        <button type="submit" className="btn btn-sm btn-primary" disabled={busy || isSubmitting}>
          {busy || isSubmitting ? "Saving…" : isEdit ? "Save changes" : "Create code"}
        </button>
        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
      </div>
    </form>
  );
}

function blank(value: number | null | undefined): string {
  return value === null || value === undefined ? "" : String(value);
}

/** An ISO instant as the local `datetime-local` value a browser's date control expects. */
function toLocalInput(iso: string): string {
  const date = new Date(iso);

  if (Number.isNaN(date.getTime())) {
    return "";
  }

  const offset = date.getTimezoneOffset() * 60_000;

  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

function inThirtyDays(): string {
  const date = new Date();
  date.setDate(date.getDate() + 30);

  return date.toISOString();
}
