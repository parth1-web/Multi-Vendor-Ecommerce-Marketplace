"use client";

/**
 * Editing an existing listing.
 *
 * The product's own fields come from the API and are editable here. Its images, variants and
 * specifications are not: they are separate resources with their own rules, and quietly
 * re-sending them on every save would rewrite stock records a seller never meant to touch. The
 * page says so rather than pretending to edit everything.
 */

import Link from "next/link";
import { useParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";

import { ErrorState } from "@/components/shared/Feedback";
import { ProductEditor } from "@/features/seller/components/ProductEditor";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { formatCurrency } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";

export function SellerProductEdit() {
  const { id } = useParams<{ id: string }>();

  const products = useQuery({
    queryKey: queryKeys.seller.products({ page: 1 }),
    queryFn: () => sellerApi.products({ page: 1, pageSize: 100 }),
    enabled: Boolean(id),
  });

  const product = products.data?.items.find(item => item.id === id);

  if (products.isPending) {
    return <div className="mp-skeleton" style={{ height: "24rem", borderRadius: "var(--radius)" }} />;
  }

  if (products.isError || !product) {
    return <ErrorState message="We could not find that product in your catalogue. It may have been deleted." />;
  }

  return (
    <div className="mp-stack">
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">{product.name}</h1>
          <p className="mp-page-subtitle">
            {product.status} · {formatCurrency(product.basePrice, undefined)} · {product.availableQuantity} in stock
          </p>
        </div>
      </div>

      {product.status === "PendingApproval" ? (
        <p className="mp-alert mp-alert-info">
          This listing is with a moderator. Editing it does not withdraw the review, and changes are not visible to
          shoppers until it is approved.
        </p>
      ) : null}

      {product.status === "Rejected" && product.rejectionNote ? (
        <p className="mp-alert mp-alert-warning">
          A moderator sent this back: {product.rejectionNote}
        </p>
      ) : null}

      <p className="mp-alert mp-alert-info">
        Images, variants and stock are managed from the product page itself — changing them here would rewrite
        inventory records you did not mean to touch.{" "}
        <Link href={`/seller/products/${product.id}`}>Open the product</Link>
        {" "}to manage them.
      </p>

      <ProductEditor productId={product.id} />
    </div>
  );
}
