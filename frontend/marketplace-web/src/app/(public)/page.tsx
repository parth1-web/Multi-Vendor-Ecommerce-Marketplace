import Link from "next/link";
import { ArrowRight } from "lucide-react";
import { Suspense } from "react";

import { PageHero } from "@/components/shared/PageHero";
import { TrustStrip } from "@/components/shared/TrustStrip";
import {
  CategoryRailSection,
  CategoryRailSkeleton,
  DealsSpotlightSection,
  DealsSpotlightSkeleton,
  HeroMediaSkeleton,
  HeroShowcaseSection,
  NewArrivalsSection,
  ProductRailSkeleton,
  StoreRailSection,
  StoreRailSkeleton,
  TrendingRailSection,
} from "@/components/home/HomeSections";
import { MARKETPLACE_TRUST_ITEMS } from "@/components/shared/TrustStrip";
import { pageMetadata } from "@/lib/seo";

export const metadata = pageMetadata({
  title: "Marketplace — Everything you need from sellers you trust",
  description:
    "Discover quality products from independent sellers, browse live categories and stores, compare listings, and check out once.",
  path: "/",
});

/**
 * The homepage, rendered on the server.
 *
 * Copy and trust content render immediately, while each data section streams inside its own
 * Suspense boundary. A slow or failed section shows its skeleton or is omitted; it never takes
 * the page down with it.
 */
export default function HomePage() {
  const heroTrust = MARKETPLACE_TRUST_ITEMS.slice(0, 3);

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-6)" }}>
      <PageHero
        id="marketplace-hero"
        eyebrow="Your one-stop marketplace"
        title="Everything you need. From sellers you trust."
        description="Discover quality products from independent sellers. Compare listings, keep one basket, and check out once."
        primaryAction={{ href: "/products", label: "Shop Now" }}
        secondaryAction={{ href: "/stores", label: "Explore Stores" }}
        media={
          <Suspense fallback={<HeroMediaSkeleton />}>
            <HeroShowcaseSection />
          </Suspense>
        }
      />

      <ul className="mp-hero-trust" aria-label="Marketplace guarantees">
        {heroTrust.map((item) => (
          <li key={item.title}>
            <item.icon size={16} aria-hidden />
            <span>{item.title}</span>
          </li>
        ))}
      </ul>

      <Suspense fallback={<CategoryRailSkeleton />}>
        <CategoryRailSection />
      </Suspense>

      <TrustStrip />

      <div className="mp-stack" style={{ gap: "var(--space-7)", marginTop: "var(--space-7)" }}>
        <Suspense fallback={<ProductRailSkeleton />}>
          <TrendingRailSection />
        </Suspense>

        <Suspense fallback={<DealsSpotlightSkeleton />}>
          <DealsSpotlightSection />
        </Suspense>

        <Suspense fallback={<StoreRailSkeleton />}>
          <StoreRailSection />
        </Suspense>

        <Suspense fallback={<ProductRailSkeleton />}>
          <NewArrivalsSection />
        </Suspense>
      </div>

      <SellCallToAction />
    </div>
  );
}

function SellCallToAction() {
  return (
    <section
      className="mp-card"
      style={{
        padding: "var(--space-6)",
        marginTop: "var(--space-7)",
        display: "flex",
        flexWrap: "wrap",
        gap: "var(--space-4)",
        alignItems: "center",
        justifyContent: "space-between",
      }}
    >
      <div>
        <h2 className="mp-section-title" style={{ margin: 0 }}>
          Sell on the marketplace
        </h2>
        <p style={{ color: "var(--text-muted)", marginBottom: 0 }}>
          Open a store, list your catalogue, and take payment without building any of this yourself.
        </p>
      </div>

      {/* The registration form asks whether the shopper wants to sell, so the link goes to the
          form itself rather than pretending a query parameter can preselect it. */}
      <Link href="/register" className="btn btn-primary">
        Start selling <ArrowRight size={16} aria-hidden />
      </Link>
    </section>
  );
}
