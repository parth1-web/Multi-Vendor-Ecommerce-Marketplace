/**
 * A product detail page, rendered on the server.
 *
 * This is the page a search engine indexes and a shopper shares, so the data is fetched here
 * with a cache tag rather than in a client effect. Only the buy box is a client component.
 */

import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { Breadcrumbs } from "@/components/navigation/Breadcrumbs";
import { RatingStars } from "@/components/shared/RatingStars";
import { PriceDisplay } from "@/components/shared/PriceDisplay";
import { StatusBadge } from "@/components/shared/Feedback";
import { SectionHeader } from "@/components/shared/SectionHeader";
import { ProductPhoto } from "@/components/products/ProductPhoto";
import { SellerCard } from "@/components/stores/SellerCard";
import { BuyBox } from "@/features/products/components/BuyBox";
import { ProductGallery } from "@/features/products/components/ProductGallery";
import { ProductReviews } from "@/features/products/components/ProductReviews";
import { ReviewItem } from "@/features/products/components/ReviewItem";
import { ApiError, serverGet } from "@/lib/serverApi";
import { formatDate } from "@/lib/format";
import { breadcrumbJsonLd, canonical, jsonLdScript, pageMetadata } from "@/lib/seo";
import type { ProductDetail, RatingBreakdown } from "@/types/product";

export const dynamic = "force-dynamic";

type Params = Promise<{ slug: string }>;
type SearchParams = Promise<Record<string, string | string[] | undefined>>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { slug } = await params;

  try {
    const product = await load(slug);
    const description = product.shortDescription || product.description.slice(0, 155);

    return pageMetadata({
      title: product.name,
      description,
      path: `/products/${product.slug}`,
      image: primaryImage(product),
    });
  } catch {
    // A page that cannot describe itself must not invent a title.
    return { title: "Product not found", robots: { index: false, follow: true } };
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
  const crumbs = [
    { name: "Home", path: "/" },
    { name: product.categoryName, path: `/categories/${product.categorySlug}` },
    { name: product.name, path: `/products/${product.slug}` },
  ];

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <script
        type="application/ld+json"
        // The payload is built from our own API and escaped, so it cannot close the script tag.
        dangerouslySetInnerHTML={{ __html: jsonLdScript(productJsonLd(product, crumbs)) }}
      />

      <Breadcrumbs
        trail={crumbs.map((crumb, index) =>
          index === crumbs.length - 1 ? { name: crumb.name } : { name: crumb.name, href: crumb.path },
        )}
      />


      <div className="row g-4">
        <div className="col-12 col-lg-7">
          <ProductGallery images={product.images} productName={product.name} />
        </div>

        <div className="col-12 col-lg-5">
          <div className="mp-stack">
            <div>
              <p className="mp-metric-label" style={{ margin: 0 }}>
                {product.categoryName}
                {product.brand ? ` · ${product.brand}` : ""}
              </p>

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

            <SellerCard
              storeName={product.storeName}
              storeSlug={product.storeSlug}
              storeLogoUrl={product.storeLogoUrl}
              storeRating={product.storeRating}
              storeRatingCount={product.storeRatingCount}
              sellerName={product.sellerName}
            />

            <div className="mp-sticky-buy">
              <BuyBox product={product} initialVariantId={variant} />
            </div>

            <dl className="mp-stack-sm mb-0" style={{ fontSize: "var(--fs-sm)" }}>
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

            <RatingSummary breakdown={product.ratingBreakdown} />

            {product.reviews.length === 0 ? (
              <p style={{ color: "var(--text-muted)" }}>
                No reviews for this product yet. If you have bought it, you can write one from{" "}
                <Link href="/orders">your orders</Link>.
              </p>
            ) : (
              <ul className="list-unstyled mp-stack" style={{ marginBottom: 0 }}>
                {product.reviews.map((review) => (
                  <li key={review.id} style={{ borderBottom: "1px solid var(--border)", paddingBottom: "var(--space-3)" }}>
                    <ReviewItem
                      rating={review.rating}
                      title={review.title}
                      body={review.body}
                      authorName={review.authorName}
                      isVerifiedPurchase={review.isVerifiedPurchase}
                      createdAt={review.createdAt}
                      reply={review.reply}
                    />
                  </li>
                ))}
              </ul>
            )}

            <ProductReviews productId={product.id} totalCount={product.reviewCount} />
          </section>
        </div>
      </div>

      {product.relatedProducts.length > 0 ? (
        <section aria-labelledby="related" className="mt-5">
          <SectionHeader
            id="related"
            title="You might also like"
            description={`More from ${product.categoryName}.`}
            actionHref={`/categories/${product.categorySlug}`}
            actionLabel="More in this category"
          />

          <div className="row g-3">
            {product.relatedProducts.map((related) => (
              <div key={related.id} className="col-6 col-md-4 col-xl-3">
                <article className="mp-card mp-card-hover" style={{ padding: "var(--space-3)", height: "100%" }}>
                  <Link href={`/products/${related.slug}`} style={{ color: "var(--text)" }}>
                    <ProductPhoto
                      src={related.primaryImageUrl}
                      alt={related.name}
                      width={480}
                      height={480}
                      style={{ backgroundColor: "var(--bg-subtle)" }}
                    />

                    <p style={{ margin: "var(--space-2) 0 0", fontSize: "var(--fs-sm)", fontWeight: 500 }}>{related.name}</p>
                    <p style={{ margin: "var(--space-1) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{related.storeName}</p>
                    <RatingStars rating={related.ratingAverage} count={related.ratingCount} />
                    <PriceDisplay price={related.basePrice} compareAtPrice={related.compareAtPrice} discountPercentage={related.discountPercentage} />
                  </Link>
                </article>
              </div>
            ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}

/**
 * The rating distribution, as bars rather than a number.
 *
 * An average on its own hides the shape behind it: a product with a 4.2 average can be four
 * fives and a pile of ones, and those two products deserve different decisions.
 */
function RatingSummary({ breakdown }: { breakdown: RatingBreakdown }) {
  if (breakdown.total === 0) {
    return null;
  }

  const rows = [
    { stars: 5, count: breakdown.fiveStar },
    { stars: 4, count: breakdown.fourStar },
    { stars: 3, count: breakdown.threeStar },
    { stars: 2, count: breakdown.twoStar },
    { stars: 1, count: breakdown.oneStar },
  ];

  return (
    <div className="d-flex align-items-center mb-3" style={{ gap: "var(--space-4)" }}>
      <div style={{ textAlign: "center" }}>
        <p style={{ margin: 0, fontSize: "var(--fs-h1)", fontWeight: 700, lineHeight: 1 }}>{breakdown.average.toFixed(1)}</p>
        <RatingStars rating={breakdown.average} showCount={false} />
        <p style={{ margin: "0.25rem 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{breakdown.total} reviews</p>
      </div>

      <ul className="list-unstyled flex-grow-1 mb-0">
        {rows.map((row) => (
          <li key={row.stars} className="d-flex align-items-center" style={{ gap: "var(--space-2)" }}>
            <span className="mp-metric-label" style={{ minWidth: "2.5rem" }}>
              {row.stars}★
            </span>
            <span
              className="flex-grow-1"
              role="presentation"
              style={{ height: "0.5rem", borderRadius: "999px", backgroundColor: "var(--bg-subtle)", overflow: "hidden" }}
            >
              <span
                style={{
                  display: "block",
                  height: "100%",
                  width: `${Math.round((row.count / breakdown.total) * 100)}%`,
                  backgroundColor: "var(--violet)",
                }}
              />
            </span>
            <span style={{ minWidth: "2rem", textAlign: "right", color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>{row.count}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** The image a share card shows: the one marked primary, or failing that, the first. */
function primaryImage(product: ProductDetail): string | null {
  return product.images.find((image) => image.isPrimary)?.url ?? product.images[0]?.url ?? null;
}

/**
 * The product as structured data.
 *
 * Price, availability and rating are the three things a search result shows, and all three are
 * here facts rather than text to be scraped. The offer is the cheapest sellable variant, which
 * is what a shopper would actually be asked to pay.
 */
function productJsonLd(product: ProductDetail, crumbs: { name: string; path: string }[]) {
  const cheapest = product.variants
    .filter((variant) => variant.isActive && variant.availableQuantity > 0)
    .sort((a, b) => a.price - b.price)[0];

  const price = cheapest?.price ?? product.basePrice;

  return {
    "@context": "https://schema.org",
    "@type": "Product",
    name: product.name,
    description: product.shortDescription || product.description.slice(0, 300),
    sku: cheapest?.sku,
    image: product.images.map((image) => image.url),
    category: product.categoryName,
    brand: product.brand ? { "@type": "Brand", name: product.brand } : undefined,
    offers: {
      "@type": "Offer",
      url: canonical(`/products/${product.slug}`),
      price: price.toFixed(2),
      priceCurrency: "USD",
      availability: product.availableQuantity > 0 ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
      seller: { "@type": "Organization", name: product.storeName, url: canonical(`/stores/${product.storeSlug}`) },
    },
    aggregateRating:
      product.ratingCount > 0
        ? { "@type": "AggregateRating", ratingValue: product.ratingAverage, reviewCount: product.ratingCount }
        : undefined,
    breadcrumb: breadcrumbJsonLd(crumbs),
  };
}


/** A variant can carry its own price; the product price is the fallback. */
function variantPrice(product: ProductDetail, variantId: string | undefined): number {
  const variant = variantId ? product.variants.find((candidate) => candidate.id === variantId) : null;
  return variant?.price ?? product.basePrice;
}

async function load(slug: string): Promise<ProductDetail> {
  return serverGet<ProductDetail>(`/api/products/slug/${encodeURIComponent(slug)}`, 300);
}
