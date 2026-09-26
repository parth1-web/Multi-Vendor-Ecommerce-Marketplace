/**
 * A product detail page, rendered on the server.
 *
 * This is the page a search engine indexes and a shopper shares, so the data is fetched here
 * with a cache tag rather than in a client effect. Only the buy box is a client component.
 */

import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ChevronRight, Store } from "lucide-react";

import { RatingStars } from "@/components/shared/RatingStars";
import { PriceDisplay } from "@/components/shared/PriceDisplay";
import { StatusBadge } from "@/components/shared/Feedback";
import { BuyBox } from "@/features/products/components/BuyBox";
import { ApiError, serverGet } from "@/lib/serverApi";
import { formatDate } from "@/lib/format";
import type { ProductDetail } from "@/types/product";

type Params = Promise<{ slug: string }>;
type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { slug } = await params;

  try {
    const product = await load(slug);
    const description = product.shortDescription || product.description.slice(0, 155);

    return {
      title: product.name,
      description,
      openGraph: {
        title: product.name,
        description,
        images: product.primaryImageUrl ? [{ url: product.primaryImageUrl, alt: product.name }] : undefined,
      },
    };
  } catch {
    // A page that cannot describe itself must not invent a title.
    return { title: "Product not found" };
  }
}

export default async function ProductPage({ params, searchParams }: { params: Params; searchParams: SearchParams }) {
  const { slug } = await params;
  const query = await searchParams;

  let product: ProductDetail;

  try {
    product = await load(slug);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }

    throw error;
  }

  const variant = query.variant as string | undefined;

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <nav aria-label="Breadcrumb" className="mb-3">
        <ol className="list-unstyled d-flex align-items-center flex-wrap mb-0" style={{ gap: "0.35rem", fontSize: "var(--fs-xs)", color: "var(--text-muted)" }}>
          <li>
            <Link href="/">Home</Link>
          </li>
          <ChevronRight size={12} aria-hidden />
          <li>
            <Link href={`/products?categorySlug=${product.categorySlug}`}>{product.categoryName}</Link>
          </li>
          <ChevronRight size={12} aria-hidden />
          <li aria-current="page" style={{ color: "var(--text)" }}>
            {product.name}
          </li>
        </ol>
      </nav>

      <div className="row g-4">
        <div className="col-12 col-lg-7">
          <Gallery product={product} />
        </div>

        <div className="col-12 col-lg-5">
          <div className="mp-stack">
            <div>
              <Link
                href={`/stores/${product.storeSlug}`}
                className="d-inline-flex align-items-center"
                style={{ gap: "0.35rem", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}
              >
                <Store size={14} aria-hidden />
                {product.storeName}
              </Link>

              <h1 className="mp-page-title" style={{ fontSize: "var(--fs-h1)", marginTop: "var(--space-2)" }}>
                {product.name}
              </h1>

              <div className="d-flex align-items-center flex-wrap mt-2" style={{ gap: "var(--space-3)" }}>
                <RatingStars rating={product.ratingAverage} count={product.ratingCount} />
                {product.isFeatured ? <StatusBadge tone="violet">Featured</StatusBadge> : null}
                {product.soldCount > 0 ? (
                  <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>{product.soldCount} sold</span>
                ) : null}
              </div>
            </div>

            <PriceDisplay
              price={variantPrice(product, variant)}
              compareAtPrice={product.compareAtPrice}
              discountPercentage={product.discountPercentage}
              size="lg"
            />

            <p style={{ color: "var(--text-muted)" }}>{product.shortDescription}</p>

            <BuyBox product={product} />

            <dl className="mp-stack-sm mb-0" style={{ fontSize: "var(--fs-sm)" }}>
              {product.brand ? (
                <div className="d-flex">
                  <dt className="mp-metric-label" style={{ minWidth: "7rem" }}>
                    Brand
                  </dt>
                  <dd className="mb-0">{product.brand}</dd>
                </div>
              ) : null}
              <div className="d-flex">
                <dt className="mp-metric-label" style={{ minWidth: "7rem" }}>
                  Listed
                </dt>
                <dd className="mb-0">{formatDate(product.createdAt)}</dd>
              </div>
            </dl>
          </div>
        </div>
      </div>

      <div className="row g-4 mt-2">
        <div className="col-12 col-lg-8">
          <section aria-labelledby="description">
            <h2 className="mp-section-title" id="description">
              Description
            </h2>
            <p style={{ color: "var(--text-muted)", whiteSpace: "pre-line" }}>{product.description}</p>

            {product.specifications.length > 0 ? (
              <table className="mp-table mt-3">
                <caption className="visually-hidden">Specifications</caption>
                <tbody>
                  {product.specifications.map((specification) => (
                    <tr key={`${specification.key}-${specification.sortOrder}`}>
                      <th scope="row" style={{ width: "12rem", color: "var(--text-muted)", fontWeight: 500 }}>
                        {specification.key}
                      </th>
                      <td>{specification.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : null}
          </section>
        </div>

        <div className="col-12 col-lg-4">
          <section aria-labelledby="reviews" className="mp-card" style={{ padding: "var(--space-4)" }}>
            <h2 className="mp-section-title" id="reviews">
              Reviews
            </h2>

            {product.reviews.length === 0 ? (
              <p style={{ color: "var(--text-muted)" }}>No reviews for this product yet.</p>
            ) : (
              <ul className="list-unstyled mp-stack" style={{ marginBottom: 0 }}>
                {product.reviews.map((review) => (
                  <li key={review.id} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "var(--space-3)" }}>
                    <div className="d-flex justify-content-between align-items-center">
                      <strong style={{ fontSize: "var(--fs-sm)" }}>{review.authorName}</strong>
                      <RatingStars rating={review.rating} showCount={false} />
                    </div>
                    {review.title ? <p style={{ margin: "var(--space-1) 0 0", fontWeight: 600 }}>{review.title}</p> : null}
                    <p style={{ margin: "var(--space-1) 0 0", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{review.body}</p>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      </div>
    </div>
  );
}

function Gallery({ product }: { product: ProductDetail }) {
  const [primary, ...rest] = product.images.length > 0 ? product.images : [{ id: "", url: "", altText: product.name, isPrimary: true, sortOrder: 0 }];

  return (
    <div className="mp-stack-sm">
      {primary.url ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={primary.url}
          alt={primary.altText ?? product.name}
          width={800}
          height={800}
          style={{ width: "100%", aspectRatio: "1 / 1", objectFit: "cover", borderRadius: "var(--radius)", backgroundColor: "var(--bg-subtle)" }}
        />
      ) : (
        <div className="mp-skeleton" style={{ aspectRatio: "1 / 1", borderRadius: "var(--radius)" }} />
      )}

      {rest.length > 0 ? (
        <div className="d-flex" style={{ gap: "var(--space-2)" }}>
          {rest.map((image) => (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              key={image.id}
              src={image.url}
              alt={image.altText ?? product.name}
              width={96}
              height={96}
              style={{ width: "4.5rem", height: "4.5rem", objectFit: "cover", borderRadius: "var(--radius-sm)", border: "1px solid var(--border)" }}
            />
          ))}
        </div>
      ) : null}
    </div>
  );
}

/** A variant can carry its own price; the product price is the fallback. */
function variantPrice(product: ProductDetail, variantId: string | undefined): number {
  const variant = variantId ? product.variants.find((candidate) => candidate.id === variantId) : null;
  return variant?.price ?? product.basePrice;
}

async function load(slug: string): Promise<ProductDetail> {
  return serverGet<ProductDetail>(`/api/products/slug/${encodeURIComponent(slug)}`, 300);
}
