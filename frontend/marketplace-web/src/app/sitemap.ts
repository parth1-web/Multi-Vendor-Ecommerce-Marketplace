/**
 * The sitemap: the static pages plus everything the catalogue currently holds.
 *
 * It is rebuilt hourly rather than at build time, because a marketplace's products appear and
 * disappear during the day and a sitemap frozen into a build is a sitemap of a shop that no
 * longer exists. A catalogue that cannot be read yields the static pages alone rather than an
 * empty file.
 */

import type { MetadataRoute } from "next";

import { absoluteUrl } from "@/lib/seo";
import type { Category, ProductPage } from "@/types/product";

export const revalidate = 3600;

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

const STATIC_PATHS = ["/", "/products", "/categories"];

/*
 * Storefronts are not in the sitemap: the API has no public list of stores, only one store by
 * slug. A sitemap built from a seller's own catalogue would mean crawling every seller, which
 * is a listing of the whole marketplace by another name. Storefronts are reached from the
 * products and categories that link to them.
 */

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const now = new Date();

  const entries: MetadataRoute.Sitemap = STATIC_PATHS.map((path) => ({
    url: absoluteUrl(path),
    lastModified: now,
    changeFrequency: "daily",
    priority: path === "/" ? 1 : 0.8,
  }));

  const [products, categories] = await Promise.all([loadProducts(), loadCategories()]);

  for (const product of products) {
    entries.push({
      url: absoluteUrl(`/products/${product.slug}`),
      lastModified: new Date(product.createdAt),
      changeFrequency: "weekly",
      priority: 0.7,
    });
  }

  for (const category of categories) {
    entries.push({
      url: absoluteUrl(`/categories/${category.slug}`),
      lastModified: now,
      changeFrequency: "weekly",
      priority: 0.6,
    });

    for (const child of category.children) {
      entries.push({
        url: absoluteUrl(`/categories/${child.slug}`),
        lastModified: now,
        changeFrequency: "weekly",
        priority: 0.5,
      });
    }
  }

  return entries;
}

/** Every published product, walked a page at a time up to a sane ceiling. */
async function loadProducts(): Promise<{ slug: string; createdAt: string }[]> {
  const collected: { slug: string; createdAt: string }[] = [];
  const pageSize = 100;

  for (let page = 1; page <= 20; page += 1) {
    const batch = await read<ProductPage>(`/api/products?page=${page}&pageSize=${pageSize}`);

    if (!batch || batch.items.length === 0) {
      break;
    }

    collected.push(...batch.items.map(item => ({ slug: item.slug, createdAt: item.createdAt })));

    if (collected.length >= batch.totalCount) {
      break;
    }
  }

  return collected;
}

async function loadCategories(): Promise<Category[]> {
  return (await read<Category[]>("/api/categories")) ?? [];
}

async function read<T>(path: string): Promise<T | null> {
  try {
    const response = await fetch(`${API_URL}${path}`, {
      next: { revalidate: 3600 },
      headers: { Accept: "application/json" },
    });

    return response.ok ? ((await response.json()) as T) : null;
  } catch {
    // A sitemap is a convenience. A catalogue that is down should produce a smaller sitemap,
    // not a 500 that takes the whole route with it.
    return null;
  }
}
