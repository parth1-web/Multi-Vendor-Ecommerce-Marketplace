/**
 * The category index: every top-level department and what is filed under it.
 *
 * Rendered on the server from the category tree, because this is the page a shopper lands on
 * from the header and a search engine crawls, and neither of them should have to wait for a
 * client to fetch a list that changes twice a year.
 */

import type { Metadata } from "next";
import Link from "next/link";
import { ChevronRight } from "lucide-react";

import { ApiError, serverGet } from "@/lib/serverApi";
import type { Category } from "@/types/product";

export const metadata: Metadata = {
  title: "Categories",
  description: "Every department on the marketplace, from electronics to kitchenware.",
};

const CATEGORY_TTL = 600;

// A catalogue page is rendered per request: prices, stock and sellers change during the
// day, and a snapshot baked into a build artifact is a page that lies.
export const dynamic = "force-dynamic";

export default async function CategoriesPage() {
  let categories: Category[] = [];

  try {
    categories = await serverGet<Category[]>("/api/categories", CATEGORY_TTL);
  } catch (error) {
    // A department index that cannot load is a broken navigation, not an empty marketplace: say
    // so instead of rendering a page that looks deliberately empty.
    if (!(error instanceof ApiError)) {
      throw error;
    }
  }

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Shop by category</h1>
          <p className="mp-page-subtitle">Every department, and everything filed inside it.</p>
        </div>
      </div>

      {categories.length === 0 ? (
        <p style={{ color: "var(--text-muted)" }}>The categories are unavailable at the moment.</p>
      ) : (
        <div className="row g-3">
          {categories.map((category) => (
            <div key={category.id} className="col-12 col-md-6 col-lg-4">
              <section className="mp-card" style={{ padding: "var(--space-4)", height: "100%" }}>
                <Link href={`/categories/${category.slug}`} className="d-flex align-items-center justify-content-between" style={{ color: "var(--text)" }}>
                  <span style={{ fontWeight: 600, fontSize: "var(--fs-h3)" }}>{category.name}</span>
                  <ChevronRight size={18} aria-hidden style={{ color: "var(--text-subtle)" }} />
                </Link>

                {category.description ? (
                  <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)", margin: "var(--space-2) 0 0" }}>{category.description}</p>
                ) : null}

                <p style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", margin: "var(--space-2) 0 0" }}>
                  {category.productCount} {category.productCount === 1 ? "product" : "products"}
                </p>

                {category.children.length > 0 ? (
                  <ul className="list-unstyled mp-stack-sm" style={{ marginTop: "var(--space-3)", marginBottom: 0 }}>
                    {category.children.map((child) => (
                      <li key={child.id}>
                        <Link href={`/categories/${child.slug}`} style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
                          {child.name}
                        </Link>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </section>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
