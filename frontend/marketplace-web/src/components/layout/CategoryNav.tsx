import Link from "next/link";

import { selectStorefrontCategories } from "@/lib/categories";
import { serverGetQuietly } from "@/lib/serverApi";
import type { Category } from "@/types/product";

/**
 * The category row beneath the header.
 *
 * Categories are the API's tree, not a static list: a department that becomes empty or inactive
 * disappears from this row automatically. The row scrolls horizontally rather than wrapping, so
 * it stays one line tall on a desktop and remains reachable on a phone without page overflow.
 */
export async function CategoryNav({ max = 10 }: { max?: number }) {
  const categories = await serverGetQuietly<Category[]>("/api/categories", 300);
  const items = selectStorefrontCategories(categories, max);

  if (items.length === 0) {
    return null;
  }

  return (
    <nav aria-label="Categories" className="mp-category-nav">
      <div className="mp-page">
        <div className="mp-category-scroller" role="list">
          <Link role="listitem" href="/products" className="mp-category-link">
            All products
          </Link>

          {items.map((category) => (
            <Link key={category.id} role="listitem" href={`/categories/${category.slug}`} className="mp-category-link">
              {category.name}
            </Link>
          ))}

          <Link role="listitem" href="/deals" className="mp-category-link">
            Deals
          </Link>
        </div>
      </div>
    </nav>
  );
}
