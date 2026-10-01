"use client";

/**
 * The seller's own storefront, as shoppers see it, and the form that changes it.
 *
 * Identity first, editing second: the status, commission rate and rating are backend facts the
 * form cannot change, so they sit in a read-only panel above the nine fields the update endpoint
 * accepts. Validation mirrors the server rules field for field — a name and description are
 * required, the email must parse, the founding year must be a real year — so a rejection is
 * never a surprise the form could have caught.
 */

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { z } from "zod";

import { TextAreaField, TextField } from "@/components/forms/FormField";
import { ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { errorMessage, fieldErrors } from "@/lib/errors";
import { formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";

const storeSchema = z
  .object({
    name: z.string().trim().min(1, "Name your store.").max(200),
    description: z.string().trim().min(1, "Describe what the store sells.").max(4000),
    logoUrl: z.string().trim().max(2000).optional().or(z.literal("")),
    bannerUrl: z.string().trim().max(2000).optional().or(z.literal("")),
    supportEmail: z.string().trim().email("Enter a valid email address.").optional().or(z.literal("")),
    supportPhone: z.string().trim().max(32).optional().or(z.literal("")),
    returnPolicy: z.string().trim().max(4000).optional().or(z.literal("")),
    shippingPolicy: z.string().trim().max(4000).optional().or(z.literal("")),
    foundedYear: z.string().trim().optional().or(z.literal("")),
  })
  .refine(
    (values) =>
      values.foundedYear === undefined ||
      values.foundedYear === "" ||
      (/^\d{4}$/.test(values.foundedYear) && Number(values.foundedYear) >= 1800 && Number(values.foundedYear) <= 2100),
    { message: "Enter a year between 1800 and 2100.", path: ["foundedYear"] },
  );

type StoreValues = z.infer<typeof storeSchema>;

export function SellerStore() {
  const store = useQuery({ queryKey: queryKeys.seller.ownStore(), queryFn: () => sellerApi.ownStore() });
  const identity = useQuery({ queryKey: queryKeys.seller.me(), queryFn: () => sellerApi.me() });

  if (store.isPending) {
    return (
      <div className="mp-stack">
        <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} />
        <div className="mp-skeleton" style={{ height: "22rem", borderRadius: "var(--radius)" }} />
      </div>
    );
  }

  if (store.isError || !store.data) {
    return <ErrorState message="We could not load your store." onRetry={() => void store.refetch()} />;
  }

  return (
    <div className="mp-stack">
      <SellerIdentityPanel
        businessName={identity.data?.businessName ?? null}
        status={identity.data?.status ?? null}
        commissionRate={identity.data?.defaultCommissionRate ?? null}
        rating={store.data.ratingAverage}
        ratingCount={store.data.ratingCount}
        productCount={store.data.productCount}
        salesCount={store.data.totalSalesCount}
      />

      <StoreForm
        key={store.data.storeId}
        initial={{
          name: store.data.name,
          description: store.data.description,
          logoUrl: store.data.logoUrl ?? "",
          bannerUrl: store.data.bannerUrl ?? "",
          supportEmail: store.data.supportEmail ?? "",
          supportPhone: store.data.supportPhone ?? "",
          returnPolicy: store.data.returnPolicy ?? "",
          shippingPolicy: store.data.shippingPolicy ?? "",
          foundedYear: store.data.foundedYear?.toString() ?? "",
        }}
        slug={store.data.slug}
      />
    </div>
  );
}

/** Backend facts about the seller account: shown, never edited here. */
function SellerIdentityPanel({
  businessName,
  status,
  commissionRate,
  rating,
  ratingCount,
  productCount,
  salesCount,
}: {
  businessName: string | null;
  status: string | null;
  commissionRate: number | null;
  rating: number;
  ratingCount: number;
  productCount: number;
  salesCount: number;
}) {
  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="seller-identity">
      <h2 className="mp-section-title" id="seller-identity" style={{ fontSize: "var(--fs-h3)" }}>
        Your seller account
      </h2>

      <dl className="row g-2 mb-0" style={{ fontSize: "var(--fs-sm)" }}>
        <div className="col-12 col-sm-6 col-lg-3">
          <dt className="mp-metric-label">Business</dt>
          <dd className="mb-0">{businessName ?? "—"}</dd>
        </div>
        <div className="col-12 col-sm-6 col-lg-3">
          <dt className="mp-metric-label">Status</dt>
          <dd className="mb-0">
            {status ? <StatusBadge tone={status === "Active" ? "success" : "warning"}>{status}</StatusBadge> : "—"}
          </dd>
        </div>
        <div className="col-12 col-sm-6 col-lg-3">
          <dt className="mp-metric-label">Commission rate</dt>
          <dd className="mb-0">{commissionRate !== null ? `${commissionRate}%` : "—"}</dd>
        </div>
        <div className="col-12 col-sm-6 col-lg-3">
          <dt className="mp-metric-label">Store rating</dt>
          <dd className="mb-0">
            {ratingCount > 0 ? `${rating.toFixed(1)} (${formatNumber(ratingCount)})` : "No reviews yet"}
          </dd>
        </div>
        <div className="col-12 col-sm-6 col-lg-3">
          <dt className="mp-metric-label">Live products</dt>
          <dd className="mb-0">{formatNumber(productCount)}</dd>
        </div>
        <div className="col-12 col-sm-6 col-lg-3">
          <dt className="mp-metric-label">Total sales</dt>
          <dd className="mb-0">{formatNumber(salesCount)}</dd>
        </div>
      </dl>
    </section>
  );
}

function StoreForm({ initial, slug }: { initial: StoreValues; slug: string }) {
  const queryClient = useQueryClient();
  const { push } = useToast();
  const [formError, setFormError] = useState<string | null>(null);
  // Live previews without the form's watch(): the compiler cannot memoize that API, while plain
  // change handlers are just state.
  const [logoPreview, setLogoPreview] = useState(initial.logoUrl ?? "");
  const [bannerPreview, setBannerPreview] = useState(initial.bannerUrl ?? "");

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<StoreValues>({ resolver: zodResolver(storeSchema), defaultValues: initial });

  async function onSubmit(values: StoreValues) {
    setFormError(null);

    try {
      await sellerApi.updateOwnStore({
        name: values.name,
        description: values.description,
        logoUrl: values.logoUrl?.trim() || null,
        bannerUrl: values.bannerUrl?.trim() || null,
        supportEmail: values.supportEmail?.trim() || null,
        supportPhone: values.supportPhone?.trim() || null,
        returnPolicy: values.returnPolicy?.trim() || null,
        shippingPolicy: values.shippingPolicy?.trim() || null,
        foundedYear: values.foundedYear?.trim() ? Number(values.foundedYear) : null,
      });

      await queryClient.invalidateQueries({ queryKey: queryKeys.seller.ownStore() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.seller.me() });
      push({ tone: "success", title: "Store updated", body: "Shoppers see the new details immediately." });
    } catch (error) {
      for (const [field, message] of Object.entries(fieldErrors(error))) {
        const key = field.toLowerCase();
        if (key in values) {
          setError(key as keyof StoreValues, { message });
        }
      }

      setFormError(errorMessage(error));
    }
  }

  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="store-form">
      <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
        <h2 className="mp-section-title" id="store-form" style={{ fontSize: "var(--fs-h3)", margin: 0 }}>
          Storefront details
        </h2>
        <Link href={`/stores/${slug}`} className="btn btn-sm btn-outline-secondary">
          View public store
        </Link>
      </div>

      <form onSubmit={handleSubmit(onSubmit)} noValidate className="mp-stack">
        {formError ? (
          <p role="alert" className="mp-alert mp-alert-danger">
            {formError}
          </p>
        ) : null}

        <div className="row g-2">
          <div className="col-12 col-sm-6">
            <TextField label="Store name" required error={errors.name?.message} {...register("name")} />
          </div>
          <div className="col-12 col-sm-6">
            <TextField
              label="Founded year"
              inputMode="numeric"
              placeholder="e.g. 2019"
              error={errors.foundedYear?.message}
              {...register("foundedYear")}
            />
          </div>
        </div>

        <TextAreaField
          label="Description"
          required
          rows={4}
          hint="What the store sells, in a sentence or two. Shoppers read this on the storefront."
          error={errors.description?.message}
          {...register("description")}
        />

        <div className="row g-2">
          <div className="col-12 col-sm-6">
            <TextField
              label="Logo image URL"
              type="url"
              placeholder="https://…"
              error={errors.logoUrl?.message}
              {...register("logoUrl", { onChange: (event) => setLogoPreview(event.target.value) })}
            />
            {logoPreview.trim() ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                src={logoPreview.trim()}
                alt="Logo preview"
                width={56}
                height={56}
                loading="lazy"
                decoding="async"
                style={{
                  width: "3.5rem",
                  height: "3.5rem",
                  objectFit: "cover",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  marginTop: "var(--space-2)",
                }}
              />
            ) : null}
          </div>
          <div className="col-12 col-sm-6">
            <TextField
              label="Banner image URL"
              type="url"
              placeholder="https://…"
              error={errors.bannerUrl?.message}
              {...register("bannerUrl", { onChange: (event) => setBannerPreview(event.target.value) })}
            />
            {bannerPreview.trim() ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                src={bannerPreview.trim()}
                alt="Banner preview"
                width={320}
                height={90}
                loading="lazy"
                decoding="async"
                style={{
                  width: "100%",
                  height: "5rem",
                  objectFit: "cover",
                  borderRadius: "var(--radius-sm)",
                  border: "1px solid var(--border)",
                  marginTop: "var(--space-2)",
                }}
              />
            ) : null}
          </div>
        </div>

        <div className="row g-2">
          <div className="col-12 col-sm-6">
            <TextField
              label="Support email"
              type="email"
              autoComplete="email"
              error={errors.supportEmail?.message}
              {...register("supportEmail")}
            />
          </div>
          <div className="col-12 col-sm-6">
            <TextField
              label="Support phone"
              type="tel"
              autoComplete="tel"
              error={errors.supportPhone?.message}
              {...register("supportPhone")}
            />
          </div>
        </div>

        <TextAreaField
          label="Shipping policy"
          rows={3}
          hint="Shown on the storefront. Leave blank to show nothing."
          error={errors.shippingPolicy?.message}
          {...register("shippingPolicy")}
        />

        <TextAreaField
          label="Return policy"
          rows={3}
          hint="Shown on the storefront. Leave blank to show nothing."
          error={errors.returnPolicy?.message}
          {...register("returnPolicy")}
        />

        <div>
          <button type="submit" className="btn btn-sm btn-primary" disabled={isSubmitting || !isDirty}>
            {isSubmitting ? "Saving…" : "Save store details"}
          </button>
        </div>
      </form>
    </section>
  );
}

export function SellerStorePage() {
  return <SellerStore />;
}
