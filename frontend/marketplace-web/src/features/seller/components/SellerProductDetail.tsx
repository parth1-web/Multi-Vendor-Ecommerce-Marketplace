"use client";

/**
 * One product, and everything a seller can change about it after it exists.
 *
 * Images, variants and stock are separate operations with separate rules, so they are separate
 * panels rather than one "save" that rewrites all of it. The status banner at the top is the
 * thing a seller actually came to read: whether the listing is live, waiting, or was sent back.
 *
 * Anything destructive is confirmed first — removing a listing, an image or a variant cannot be
 * undone from here — and everything that succeeds says so, because a panel that silently redraws
 * leaves a seller unsure whether their click worked.
 */

import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { History, Plus, Send, Trash2 } from "lucide-react";

import { TextField } from "@/components/forms/FormField";
import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { errorMessage } from "@/lib/errors";
import { formatCurrency, formatDate, formatNumber } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { ProductStatus } from "@/types/product";

type Pending =
  | { kind: "delete"; label: string }
  | { kind: "image"; id: string; label: string }
  | { kind: "variant"; id: string; label: string }
  | null;

export function SellerProductDetail() {
  const { id } = useParams<{ id: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const queryClient = useQueryClient();
  const { push } = useToast();

  const [actionError, setActionError] = useState<string | null>(null);
  const [pending, setPending] = useState<Pending>(null);
  const [imageUrl, setImageUrl] = useState("");
  const [imageAlt, setImageAlt] = useState("");
  const [variantSku, setVariantSku] = useState("");
  const [variantName, setVariantName] = useState("");
  const [variantPrice, setVariantPrice] = useState("");
  const [variantStock, setVariantStock] = useState("0");
  const [variantThreshold, setVariantThreshold] = useState("5");

  const product = useQuery({
    queryKey: queryKeys.seller.product(id ?? ""),
    queryFn: () => sellerApi.product(id!),
    enabled: Boolean(id),
  });

  /**
   * A change to this listing makes the listing untrue, the catalogue page that lists it untrue, and
   * the dashboard's product counts untrue. Nothing else is touched: an image added should not
   * re-fetch every order and every revenue chart.
   */
  const invalidate = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.seller.product(id ?? "") }),
      queryClient.invalidateQueries({ queryKey: queryKeys.seller.productLists() }),
      queryClient.invalidateQueries({ queryKey: queryKeys.seller.summary() }),
      queryClient.invalidateQueries({ queryKey: queryKeys.products.all }),
    ]);
  };

  const fail = (error: unknown) => setActionError(errorMessage(error));

  const addImage = useMutation({
    mutationFn: () =>
      sellerApi.addProductImage(id!, {
        url: imageUrl.trim(),
        altText: imageAlt.trim() || null,
        isPrimary: false,
      }),
    onSuccess: async () => {
      setImageUrl("");
      setImageAlt("");
      setActionError(null);
      await invalidate();
      push({ tone: "success", title: "Image added" });
    },
    onError: fail,
  });

  const removeImage = useMutation({
    mutationFn: (imageId: string) => sellerApi.deleteProductImage(id!, imageId),
    onSuccess: async () => {
      setPending(null);
      setActionError(null);
      await invalidate();
      push({ tone: "success", title: "Image removed" });
    },
    onError: fail,
  });

  const addVariant = useMutation({
    mutationFn: () =>
      sellerApi.addProductVariant(id!, {
        sku: variantSku.trim(),
        name: variantName.trim() || "Default",
        price: variantPrice.trim() ? Number(variantPrice) : null,
        initialStock: Number(variantStock) || 0,
        lowStockThreshold: variantThreshold.trim() ? Number(variantThreshold) : null,
        options: [],
      }),
    onSuccess: async () => {
      setVariantSku("");
      setVariantName("");
      setVariantPrice("");
      setVariantStock("0");
      setVariantThreshold("5");
      setActionError(null);
      await invalidate();
      push({ tone: "success", title: "Variant added", body: "It now has its own stock record." });
    },
    onError: fail,
  });

  const removeVariant = useMutation({
    mutationFn: (variantId: string) => sellerApi.deleteProductVariant(id!, variantId),
    onSuccess: async () => {
      setPending(null);
      setActionError(null);
      await invalidate();
      push({ tone: "success", title: "Variant removed" });
    },
    onError: fail,
  });

  const submit = useMutation({
    mutationFn: () => sellerApi.submitForApproval(id ?? ""),
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
      push({
        tone: "success",
        title: "Sent for review",
        body: "A moderator will look at it. Shoppers cannot see it until they do.",
      });
    },
    onError: fail,
  });

  const remove = useMutation({
    mutationFn: () => sellerApi.deleteProduct(id ?? ""),
    onSuccess: async () => {
      setPending(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.seller.productLists() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.seller.summary() }),
      ]);
      push({ tone: "success", title: "Listing deleted", body: "It has been taken out of your catalogue." });
      router.push("/seller/products");
    },
    onError: fail,
  });

  if (product.isPending) {
    return <div className="mp-skeleton" style={{ height: "24rem", borderRadius: "var(--radius)" }} aria-hidden />;
  }

  if (product.isError || !product.data) {
    return (
      <ErrorState message="We could not load that product. It may have been deleted." onRetry={() => void product.refetch()} />
    );
  }

  const data = product.data;
  // An order that sent the seller here is the thing they will want to get back to.
  const fromOrder = searchParams.get("from");
  const listParams = new URLSearchParams();

  for (const key of ["status", "search"]) {
    const value = searchParams.get(key);

    if (value) {
      listParams.set(key, value);
    }
  }

  const listContext = listParams.toString();

  return (
    <div className="mp-stack">
      <div className="mp-page-header">
        <div style={{ minWidth: 0 }}>
          <h1 className="mp-page-title">{data.name}</h1>
          <div className="d-flex align-items-center flex-wrap mt-1" style={{ gap: "var(--space-3)" }}>
            <StatusBadge tone={statusTone(data.status)}>{statusLabel(data.status)}</StatusBadge>
            <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{formatCurrency(data.basePrice)}</span>
            <span style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
              {data.status === "Published" ? `Live since ${formatDate(data.publishedAt ?? data.createdAt)}` : `Created ${formatDate(data.createdAt)}`}
            </span>
          </div>
        </div>

        <div className="d-flex flex-wrap" style={{ gap: "0.5rem" }}>
          {fromOrder ? (
            <Link href={`/seller/orders/${fromOrder}`} className="btn btn-sm btn-outline-secondary">
              Back to the order
            </Link>
          ) : listContext ? (
            <Link href={`/seller/products?${listContext}`} className="btn btn-sm btn-outline-secondary">
              Back to your products
            </Link>
          ) : null}

          <Link href={`/seller/products/${data.id}/edit${listContext ? `?${listContext}` : ""}`} className="btn btn-sm btn-outline-secondary">
            Edit details
          </Link>

          {data.status === "Published" ? (
            <Link href={`/products/${data.slug}`} className="btn btn-sm btn-outline-secondary">
              View it live
            </Link>
          ) : null}
        </div>
      </div>

      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {data.status === "PendingApproval" ? (
        <p className="mp-alert mp-alert-info" role="status">
          A moderator is looking at this. Shoppers cannot see it until they do.
        </p>
      ) : null}

      {data.status === "Rejected" ? (
        <p className="mp-alert mp-alert-warning" role="status">
          Sent back{data.rejectionNote ? `: ${data.rejectionNote}` : "."} Fix it and submit again.
        </p>
      ) : null}

      {data.status === "Draft" || data.status === "Rejected" ? (
        <div>
          <button type="button" className="btn btn-sm btn-primary" onClick={() => submit.mutate()} disabled={submit.isPending}>
            <Send size={14} aria-hidden className="me-1" />
            {submit.isPending ? "Sending…" : "Submit for review"}
          </button>
        </div>
      ) : null}

      <div className="row g-4">
        <div className="col-12 col-lg-6">
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="product-images">
            <h2 className="mp-section-title" id="product-images" style={{ fontSize: "var(--fs-h3)" }}>
              Images
            </h2>

            {data.images.length === 0 ? (
              <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                No images. A listing with no picture is hard to sell.
              </p>
            ) : (
              <ul className="list-unstyled mb-0 d-flex flex-wrap" style={{ gap: "var(--space-2)" }}>
                {data.images.map(image => (
                  <li key={image.id} style={{ position: "relative" }}>
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img
                      src={image.url}
                      alt={image.altText ?? data.name}
                      width={96}
                      height={96}
                      loading="lazy"
                      decoding="async"
                      style={{
                        width: "6rem",
                        height: "6rem",
                        objectFit: "cover",
                        borderRadius: "var(--radius-sm)",
                        border: "1px solid var(--border)",
                      }}
                    />
                    {image.isPrimary ? (
                      <span
                        className="mp-badge mp-badge-info"
                        style={{ position: "absolute", bottom: "0.25rem", left: "0.25rem", fontSize: "var(--fs-xs)" }}
                      >
                        Main
                      </span>
                    ) : null}
                    <button
                      type="button"
                      className="btn btn-sm"
                      onClick={() => setPending({ kind: "image", id: image.id, label: `an image of ${data.name}` })}
                      disabled={removeImage.isPending}
                      aria-label={`Remove ${image.altText ?? `an image of ${data.name}`}`}
                      style={{
                        position: "absolute",
                        top: "0.25rem",
                        right: "0.25rem",
                        backgroundColor: "var(--bg-surface)",
                        borderRadius: "50%",
                        padding: "0.2rem",
                      }}
                    >
                      <Trash2 size={14} aria-hidden />
                    </button>
                  </li>
                ))}
              </ul>
            )}

            <div className="row g-2 mt-3">
              <div className="col-12 col-md-7">
                <TextField
                  label="Image address"
                  type="url"
                  placeholder="https://…"
                  value={imageUrl}
                  onChange={event => setImageUrl(event.target.value)}
                />
              </div>
              <div className="col-12 col-md-5">
                <TextField
                  label="Alt text"
                  hint="What a screen reader should say."
                  maxLength={300}
                  value={imageAlt}
                  onChange={event => setImageAlt(event.target.value)}
                />
              </div>
            </div>

            <button
              type="button"
              className="btn btn-sm btn-outline-secondary"
              onClick={() => addImage.mutate()}
              disabled={imageUrl.trim().length === 0 || addImage.isPending}
            >
              <Plus size={14} aria-hidden className="me-1" />
              {addImage.isPending ? "Adding…" : "Add image"}
            </button>
          </section>
        </div>

        <div className="col-12 col-lg-6">
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="product-variants">
            <h2 className="mp-section-title" id="product-variants" style={{ fontSize: "var(--fs-h3)" }}>
              Variants and stock
            </h2>

            <div className="mp-table-wrap">
              <table className="mp-table">
                <caption className="visually-hidden">Variants</caption>
                <thead>
                  <tr>
                    <th scope="col">Name</th>
                    <th scope="col">SKU</th>
                    <th scope="col" className="text-end">
                      In stock
                    </th>
                    <th scope="col" className="text-end">
                      Low at
                    </th>
                    <th scope="col">
                      <span className="visually-hidden">Actions</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {data.variants.map(variant => (
                    <tr key={variant.id}>
                      <td>
                        {variant.name}
                        {variant.isActive ? null : (
                          <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                            switched off
                          </span>
                        )}
                      </td>
                      <td style={{ fontVariantNumeric: "tabular-nums" }}>{variant.sku}</td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums" }}>
                        {variant.availableQuantity}
                      </td>
                      <td className="text-end" style={{ fontVariantNumeric: "tabular-nums", color: "var(--text-muted)" }}>
                        {variant.lowStockThreshold}
                      </td>
                      <td>
                        <div className="d-flex justify-content-end" style={{ gap: "0.35rem" }}>
                          <Link
                            href={`/seller/inventory?product=${data.id}`}
                            className="btn btn-sm btn-outline-secondary"
                            aria-label={`Stock and history for the ${variant.name} variant`}
                            title="Adjust and review this variant's stock"
                          >
                            <History size={14} aria-hidden />
                          </Link>
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-danger"
                            onClick={() => setPending({ kind: "variant", id: variant.id, label: `the ${variant.name} variant` })}
                            disabled={removeVariant.isPending}
                            aria-label={`Remove the ${variant.name} variant`}
                          >
                            <Trash2 size={14} aria-hidden />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }} className="mt-3 mb-2">
              Add a size, colour or length. A product with one option needs exactly one variant.
            </p>

            <div className="row g-2">
              <div className="col-6">
                <TextField
                  label="Name"
                  placeholder="Large / Black"
                  value={variantName}
                  onChange={event => setVariantName(event.target.value)}
                />
              </div>
              <div className="col-6">
                <TextField
                  label="SKU"
                  required
                  maxLength={64}
                  placeholder="SKU-LG-BLK"
                  value={variantSku}
                  onChange={event => setVariantSku(event.target.value)}
                />
              </div>
              <div className="col-4">
                <TextField
                  label="Stock"
                  type="number"
                  min="0"
                  step="1"
                  inputMode="numeric"
                  value={variantStock}
                  onChange={event => setVariantStock(event.target.value)}
                />
              </div>
              <div className="col-4">
                <TextField
                  label="Price"
                  type="number"
                  min="0.01"
                  step="0.01"
                  inputMode="decimal"
                  placeholder={String(data.basePrice)}
                  hint="Optional."
                  value={variantPrice}
                  onChange={event => setVariantPrice(event.target.value)}
                />
              </div>
              <div className="col-4">
                <TextField
                  label="Low at"
                  type="number"
                  min="0"
                  step="1"
                  inputMode="numeric"
                  value={variantThreshold}
                  onChange={event => setVariantThreshold(event.target.value)}
                />
              </div>
            </div>

            <button
              type="button"
              className="btn btn-sm btn-outline-secondary"
              onClick={() => addVariant.mutate()}
              disabled={variantSku.trim().length === 0 || addVariant.isPending}
            >
              <Plus size={14} aria-hidden className="me-1" />
              {addVariant.isPending ? "Adding…" : "Add variant"}
            </button>

            <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
              {formatNumber(data.variants.length)} {data.variants.length === 1 ? "variant" : "variants"} · stock for each
              one is adjusted on the <Link href={`/seller/inventory?product=${data.id}`} className="mp-link">stock page</Link>.
            </p>
          </section>
        </div>
      </div>

      <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="product-danger">
        <h2 className="mp-section-title" id="product-danger" style={{ fontSize: "var(--fs-h3)" }}>
          Remove this listing
        </h2>
        <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          Deleting takes it out of the catalogue. Orders that already contain it are unaffected.
        </p>
        <button
          type="button"
          className="btn btn-sm btn-outline-danger"
          onClick={() => setPending({ kind: "delete", label: data.name })}
          disabled={remove.isPending}
        >
          {remove.isPending ? "Deleting…" : "Delete this listing"}
        </button>
      </section>

      {pending ? (
        <ConfirmDialog
          show
          title={
            pending.kind === "delete"
              ? `Delete ${pending.label}?`
              : pending.kind === "image"
                ? "Remove this image?"
                : "Remove this variant?"
          }
          confirmLabel={
            pending.kind === "delete" ? "Delete the listing" : pending.kind === "image" ? "Remove the image" : "Remove the variant"
          }
          busy={remove.isPending || removeImage.isPending || removeVariant.isPending}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            if (pending.kind === "delete") {
              remove.mutate();
            } else if (pending.kind === "image") {
              removeImage.mutate(pending.id);
            } else {
              removeVariant.mutate(pending.id);
            }
          }}
        >
          {pending.kind === "delete" ? (
            <p className="mb-0">
              <strong>{pending.label}</strong> is removed from your catalogue and from search. Orders that already contain
              it keep their line items, so past orders and payouts are unaffected. This cannot be undone from here.
            </p>
          ) : pending.kind === "image" ? (
            <p className="mb-0">
              This picture will no longer be shown on the listing or in search results.
            </p>
          ) : (
            <p className="mb-0">
              Removing {pending.label} removes it as something a shopper can order, along with its stock record. Orders
              that already contain it are unaffected.
            </p>
          )}
        </ConfirmDialog>
      ) : null}
    </div>
  );
}

function statusLabel(status: ProductStatus): string {
  return status === "PendingApproval" ? "Awaiting review" : status;
}

function statusTone(status: ProductStatus): "success" | "warning" | "danger" | "info" {
  switch (status) {
    case "Published":
      return "success";
    case "PendingApproval":
      return "warning";
    case "Rejected":
      return "danger";
    default:
      return "info";
  }
}