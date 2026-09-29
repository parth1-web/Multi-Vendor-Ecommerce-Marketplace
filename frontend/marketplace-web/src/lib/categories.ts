import type { Category } from "@/types/product";

/**
 * Storefront category selection.
 *
 * Navigation and the homepage must agree about which departments are worth showing. Counts come
 * from the API, but a top-level total may not include nested shelves, so children are added
 * recursively. Inactive or empty departments are omitted instead of sending a shopper to an
 * empty page.
 */
export function categoryProductTotal(category: Category, seen: Set<string> = new Set()): number {
  if (seen.has(category.id)) {
    return 0;
  }

  seen.add(category.id);

  return category.productCount + category.children.reduce((total, child) => total + categoryProductTotal(child, seen), 0);
}

export function selectStorefrontCategories(
  categories: Category[] | null | undefined,
  max = 10,
): Category[] {
  if (!Array.isArray(categories)) {
    return [];
  }

  return categories
    .map((category) => ({ ...category, productCount: categoryProductTotal(category) }))
    .filter((category) => category.isActive && category.productCount > 0)
    .sort((a, b) => a.displayOrder - b.displayOrder || b.productCount - a.productCount)
    .slice(0, Math.max(0, max));
}

/** The tree as a flat list, indented by depth for filter controls. */
export function flattenCategories(categories: Category[], depth = 0): Array<Category & { depth: number }> {
  return categories.flatMap((category) => [{ ...category, depth }, ...flattenCategories(category.children ?? [], depth + 1)]);
}
