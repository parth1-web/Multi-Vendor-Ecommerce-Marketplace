/**
 * One category, and the products filed in it.
 *
 * The category is fetched on the server for its name, description and breadcrumb, and the
 * listing is handed to the same client island the global catalogue uses, scoped to this
 * category. The category in the URL is not negotiable by a filter: it is what the page is.
 */

import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { ChevronRight } from "lucide-react";

import { readProductQuery } from "@/features/products/api/readProductQuery";
import { ListingSkeleton } from "@/features/products/components/ListingSkeleton";
import { ProductBrowser } from "@/features/products/components/ProductBrowser";
import { ApiError, serverGet } from "@/lib/serverApi";
import { breadcrumbJsonLd, jsonLdScript, pageMetadata } from "@/lib/seo";
import type { Category } from "@/types/product";

export const dynamic = "force-dynamic";

type Params = Promise<{ slug: string }>;
type SearchParams = Promise<Record<string, string | string[] | undefined>>;

const CATEGORY_TTL = 600;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { slug } = await params;

  try {
    const category = await load(slug);

    return pageMetadata({
      title: category.name,
      description: category.description ?? `Everything filed under ${category.name}.`,
      path: `/categories/${category.slug}`,
      image: category.imageUrl,
    });
  } catch {
    return { title: "Category not found", robots: { index: false, follow: true } };
  }
}


export default async function CategoryPage({ params, searchParams }: { params: Params; searchParams: SearchParams }) {
  const { slug } = await params;
  const requested = readProductQuery(await searchParams);

  let category: Category;

  try {
    category = await load(slug);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }

    throw error;
  }

  // The path owns the category, so the query keeps it even if a link tried to change it.
  const query = { ...requested, categorySlug: category.slug, includeSubcategories: true };

  const crumbs = [
    { name: "Home", path: "/" },
    { name: "Categories", path: "/categories" },
    ...category.breadcrumb.map(crumb => ({ name: crumb.name, path: `/categories/${crumb.slug}` })),
  ];


  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: jsonLdScript(breadcrumbJsonLd(crumbs)) }}
      />

      <nav aria-label="Breadcrumb" className="mb-3">
        <ol className="list-unstyled d-flex align-items-center flex-wrap mb-0" style={{ gap: "0.35rem", fontSize: "var(--fs-xs)", color: "var(--text-muted)" }}>
          {crumbs.map((crumb, index) => (
            <li key={crumb.path} className="d-flex align-items-center" style={{ gap: "0.35rem" }}>
              {index > 0 ? <ChevronRight size={12} aria-hidden /> : null}
              {index === crumbs.length - 1 ? (
                <span aria-current="page" style={{ color: "var(--text)" }}>
                  {crumb.name}
                </span>
              ) : (
                <Link href={crumb.path}>{crumb.name}</Link>
              )}
            </li>
          ))}
        </ol>
      </nav>


      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">{category.name}</h1>
          <p className="mp-page-subtitle">
            {category.description ?? `${category.productCount} products across this category and its subcategories.`}
          </p>
        </div>
      </div>

      {category.children.length > 0 ? (
        <nav aria-label={`Subcategories of ${category.name}`} className="d-flex flex-wrap mb-4" style={{ gap: "var(--space-2)" }}>
          {category.children.map((child) => (
            <Link key={child.id} href={`/categories/${child.slug}`} className="btn btn-sm btn-outline-secondary">
              {child.name}
            </Link>
          ))}
        </nav>
      ) : null}

      <Suspense fallback={<ListingSkeleton />}>
        <ProductBrowser query={query} basePath={`/categories/${category.slug}`} />
      </Suspense>
    </div>
  );
}

async function load(slug: string): Promise<Category> {
  return serverGet<Category>(`/api/categories/${encodeURIComponent(slug)}`, CATEGORY_TTL);
}
