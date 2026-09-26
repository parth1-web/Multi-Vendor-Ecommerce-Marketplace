/**
 * Crawl rules.
 *
 * The private areas are listed rather than left to chance: an orders or basket page that gets
 * indexed is a privacy problem, and one that is merely unlinked is a decision nobody made on
 * purpose.
 */

import type { MetadataRoute } from "next";

import { absoluteUrl } from "@/lib/seo";

const PRIVATE_PREFIXES = [
  "/cart",
  "/checkout",
  "/orders",
  "/wishlist",
  "/profile",
  "/addresses",
  "/notifications",
  "/seller",
  "/admin",
  "/login",
  "/register",
  "/forgot-password",
];

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: "*",
        allow: "/",
        disallow: PRIVATE_PREFIXES,
      },
    ],
    sitemap: absoluteUrl("/sitemap.xml"),
    host: absoluteUrl("/"),
  };
}
