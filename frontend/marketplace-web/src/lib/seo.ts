/**
 * The one place that knows how a page describes itself.
 *
 * Metadata is assembled in three places in the app and each of them got the canonical URL and
 * the site name subtly wrong once. A share card with a relative image, or a canonical that
 * points at localhost, is worse than no card at all, so the pieces live together.
 */

import type { Metadata } from "next";

/** The public origin, used for canonical links, share images and the sitemap. */
export function siteUrl(): string {
  return (process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000").replace(/\/+$/, "");
}

export function absoluteUrl(path: string): string {
  return `${siteUrl()}${path.startsWith("/") ? path : `/${path}`}`;
}

/**
 * A canonical URL, so the same product reached through three filter combinations is indexed
 * once. The filter is left out deliberately: it is a view of the catalogue, not a page of it.
 */
export function canonical(path: string): string {
  return absoluteUrl(path.split("?")[0]);
}

export interface PageMetadata {
  title: string;
  description: string;
  path: string;
  image?: string | null;
  noIndex?: boolean;
}

/** Standard metadata for a page, with the canonical and the share card filled in. */
export function pageMetadata({ title, description, path, image, noIndex }: PageMetadata): Metadata {
  const url = canonical(path);

  return {
    title,
    description,
    alternates: { canonical: url },
    robots: noIndex ? { index: false, follow: true } : { index: true, follow: true },
    openGraph: {
      type: "website",
      siteName: "Marketplace",
      title,
      description,
      url,
      images: image ? [{ url: image, alt: title }] : undefined,
    },
    twitter: {
      card: image ? "summary_large_image" : "summary",
      title,
      description,
      images: image ? [image] : undefined,
    },
  };
}

/**
 * Structured data, rendered as a script tag.
 *
 * A product page without this tells a search engine it is a page of text; with it, the price
 * and rating are facts rather than something to be scraped out of the markup.
 */
export function jsonLdScript(data: Record<string, unknown>): string {
  return JSON.stringify(data).replace(/</g, "\\u003c");
}

export interface BreadcrumbEntry {
  name: string;
  path: string;
}

export function breadcrumbJsonLd(entries: BreadcrumbEntry[]) {
  return {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: entries.map((entry, index) => ({
      "@type": "ListItem",
      position: index + 1,
      name: entry.name,
      item: canonical(entry.path),
    })),
  };
}
