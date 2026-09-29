import Link from "next/link";
import { ArrowRight, TicketPercent } from "lucide-react";

import { ProductPhoto } from "@/components/products/ProductPhoto";
import { formatCurrency, formatDate } from "@/lib/format";
import type { ProductSummary } from "@/types/product";

interface PromoBannerProps {
  id?: string;
  eyebrow?: string;
  title: string;
  description?: string;
  couponCode?: string | null;
  couponLabel?: string | null;
  endsAt?: string | null;
  actionHref: string;
  actionLabel?: string;
  product?: ProductSummary | null;
}

/**
 * One reusable promotional banner.
 *
 * It renders only for a real promotion: a live coupon, live discounted product imagery, and a
 * link to the existing deals route. There is no separate marketing copy layer that can drift
 * away from what checkout will actually honor.
 */
export function PromoBanner({
  id = "promo-spotlight",
  eyebrow = "Limited time",
  title,
  description,
  couponCode,
  couponLabel,
  endsAt,
  actionHref,
  actionLabel = "Shop the deals",
  product,
}: PromoBannerProps) {
  return (
    <section aria-labelledby={id} className="mp-promo">
      <div className="mp-promo-copy">
        <p className="mp-promo-eyebrow">{eyebrow}</p>
        <h2 className="mp-promo-title" id={id}>
          {title}
        </h2>
        {description ? <p className="mp-promo-description">{description}</p> : null}

        {couponCode ? (
          <p className="mp-promo-code">
            <TicketPercent size={16} aria-hidden />
            <span>
              Use code <strong>{couponCode}</strong>
              {couponLabel ? (
                <>
                  <span aria-hidden> · </span>
                  {couponLabel}
                </>
              ) : null}
            </span>
            {endsAt ? <small>Ends {formatDate(endsAt)}</small> : null}
          </p>
        ) : null}

        <Link href={actionHref} className="btn btn-primary">
          {actionLabel}
          <ArrowRight size={16} aria-hidden />
        </Link>
      </div>

      {product?.primaryImageUrl ? (
        <Link
          href={`/products/${product.slug}`}
          className="mp-promo-media"
          aria-label={`${product.name}, sold by ${product.storeName}, ${formatCurrency(product.basePrice)}`}
        >
          <ProductPhoto
            src={product.primaryImageUrl}
            alt={product.name}
            width={960}
            height={720}
            aspectRatio="4 / 3"
          />
          <span className="mp-promo-caption" aria-hidden>
            <span className="mp-truncate">{product.name}</span>
            <span>{formatCurrency(product.basePrice)}</span>
          </span>
        </Link>
      ) : null}
    </section>
  );
}
