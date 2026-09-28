"use client";

/**
 * One product, and everything a seller can change about it after it exists.
 *
 * Images, variants and stock are separate operations with separate rules, so they are separate
 * panels rather than one "save" that rewrites all of it. The status banner at the top is the
 * thing a seller actually came to read: whether the listing is live, waiting, or was sent back.
 */

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Send, Trash2 } from "lucide-react";

import { ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { errorMessage } from "@/lib/errors";
import { formatCurrency, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { SellerProductDetail } from "@/types/productAuthoring";
import type { ProductStatus } from "@/types/product";

export function SellerProductDetail() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const queryClient = useQueryClient();
  const [actionError, setActionError] = useState<string | null>(null);
  const [imageUrl, setImageUrl] = useState("");
  const [variantSku, setVariantSku] = useState("");
  const [variantName, setVariantName] = useState("");
  const [variantStock, setVariantStock] = useState("0");

  const product = useQuery({
    queryKey: queryKeys.seller.product(id ?? ""),
    queryFn: () => sellerApi.product(id!),

    enabled: Boolean(id),
  });

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.seller.product(id ?? "") });
    await queryClient.invalidateQueries({ queryKey: queryKeys.seller.all });
  };

  const addImage = useMutation({
    mutationFn: () => sellerApi.addProductImage(id!, { url: imageUrl.trim(), altText: null, isPrimary: false }),
    onSuccess: async () => {
      setImageUrl("");
      await invalidate();
    },
    onError: error => setActionError(errorMessage(error)),
  });

  const removeImage = useMutation({
    mutationFn: (imageId: string) => sellerApi.deleteProductImage(id!, imageId),
    onSuccess: invalidate,
    onError: error => setActionError(errorMessage(error)),
  });

  const addVariant = useMutation({
    mutationFn: () =>
      sellerApi.addProductVariant(id!, {
        sku: variantSku.trim(),
        name: variantName.trim() || "Default",
        price: null,
        initialStock: Number(variantStock) || 0,
        lowStockThreshold: 5,
        options: [],
      }),

    onSuccess: async () => {
      setVariantSku("");
      setVariantName("");
      setVariantStock("0");
      await invalidate();
    },
    onError: error => setActionError(errorMessage(error)),
  });

  const removeVariant = useMutation({
    mutationFn: (variantId: string) => sellerApi.deleteProductVariant(id!, variantId),
    onSuccess: invalidate,
    onError: error => setActionError(errorMessage(error)),
  });

  const submit = useMutation({
    mutationFn: () => sellerApi.submitForApproval(id ?? ""),
    onSuccess: async () => {
      setActionError(null);
      await invalidate();
    },
    onError: error => setActionError(errorMessage(error)),
  });

  const remove = useMutation({
    mutationFn: () => sellerApi.deleteProduct(id ?? ""),
    onSuccess: () => router.push("/seller/products"),
    onError: error => setActionError(errorMessage(error)),
  });

  if (product.isPending) {
    return <div className="mp-skeleton" style={{ height: "24rem", borderRadius: "var(--radius)" }} />;
  }

  if (product.isError || !product.data) {
    return <ErrorState message="We could not load that product. It may have been deleted." />;
  }

  const data = product.data;

  return (
    <div className="mp-stack">
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">{data.name}</h1>
          <div className="d-flex align-items-center flex-wrap mt-1" style={{ gap: "var(--space-3)" }}>
            <StatusBadge tone={statusTone(data.status)}>{statusLabel(data.status)}</StatusBadge>
            <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{formatCurrency(data.basePrice, undefined)}</span>
            <span style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
              {data.status === "Published" ? `Live since ${formatDate(data.createdAt)}` : `Created ${formatDate(data.createdAt)}`}
            </span>
          </div>
        </div>

        <div className="d-flex" style={{ gap: "0.5rem" }}>
          <Link href={`/seller/products/${data.id}/edit`} className="btn btn-sm btn-outline-secondary">
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
        <p className="mp-alert mp-alert-info">
          A moderator is looking at this. Shoppers cannot see it until they do.
        </p>
      ) : null}

      {data.status === "Rejected" ? (
        <p className="mp-alert mp-alert-warning">
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
              <div className="d-flex flex-wrap" style={{ gap: "var(--space-2)" }}>
                {data.images.map(image => (
                  <div key={image.id} style={{ position: "relative" }}>
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img
                      src={image.url}
                      alt={image.altText ?? data.name}
                      width={96}
                      height={96}
                      style={{ width: "6rem", height: "6rem", objectFit: "cover", borderRadius: "var(--radius-sm)", border: "1px solid var(--border)" }}
                    />
                    <button
                      type="button"
                      className="btn btn-sm"
                      onClick={() => removeImage.mutate(image.id)}
                      disabled={removeImage.isPending}
                      aria-label="Remove this image"
                      style={{ position: "absolute", top: "0.25rem", right: "0.25rem", backgroundColor: "var(--bg-surface)", borderRadius: "50%", padding: "0.2rem" }}
                    >
                      <Trash2 size={14} aria-hidden />
                    </button>
                  </div>
                ))}
              </div>
            )}

            <div className="d-flex mt-3" style={{ gap: "0.5rem" }}>
              <input
                className="mp-input"
                type="url"
                placeholder="https://…"
                aria-label="Image address"
                value={imageUrl}
                onChange={event => setImageUrl(event.target.value)}
              />
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => addImage.mutate()} disabled={!imageUrl.trim() || addImage.isPending}>
                <Plus size={14} aria-hidden className="me-1" />
                Add
              </button>
            </div>
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
                    <th scope="col">In stock</th>
                    <th scope="col" />
                  </tr>
                </thead>
                <tbody>
                  {data.variants.map(variant => (
                    <tr key={variant.id}>
                      <td>
                        {variant.name}
                        {variant.isActive ? null : (
                          <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>switched off</span>
                        )}
                      </td>
                      <td style={{ fontVariantNumeric: "tabular-nums" }}>{variant.sku}</td>
                      <td style={{ fontVariantNumeric: "tabular-nums" }}>{variant.availableQuantity}</td>
                      <td>
                        <button
                          type="button"
                          className="btn btn-sm"
                          onClick={() => removeVariant.mutate(variant.id)}
                          disabled={removeVariant.isPending}
                          aria-label={`Remove the ${variant.name} variant`}
                          style={{ color: "var(--text-subtle)" }}
                        >
                          <Trash2 size={14} aria-hidden />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="row g-2 mt-2">
              <div className="col-6">
                <input
                  className="mp-input"
                  placeholder="Large / Black"
                  aria-label="New variant name"
                  value={variantName}
                  onChange={event => setVariantName(event.target.value)}
                />
              </div>
              <div className="col-6">
                <input
                  className="mp-input"
                  placeholder="SKU-LG-BLK"
                  aria-label="New variant SKU"
                  value={variantSku}
                  onChange={event => setVariantSku(event.target.value)}
                />
              </div>
              <div className="col-6">
                <input
                  className="mp-input"
                  type="number"
                  min="0"
                  aria-label="New variant stock"
                  value={variantStock}
                  onChange={event => setVariantStock(event.target.value)}
                />
              </div>
              <div className="col-6">
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  onClick={() => addVariant.mutate()}
                  disabled={!variantSku.trim() || addVariant.isPending}
                >
                  <Plus size={14} aria-hidden className="me-1" />
                  Add variant
                </button>
              </div>
            </div>
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
        <button type="button" className="btn btn-sm" onClick={() => remove.mutate()} disabled={remove.isPending} style={{ color: "var(--danger)" }}>
          {remove.isPending ? "Deleting…" : "Delete this listing"}
        </button>
      </section>
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
