import Link from "next/link";
import {
  ArrowRight,
  Headphones,
  Home,
  Shirt,
  ShoppingBag,
  Smartphone,
  Tag,
  Tv,
  Watch,
  type LucideIcon,
} from "lucide-react";

import { ProductPhoto } from "@/components/products/ProductPhoto";
import type { Category } from "@/types/product";

/**
 * One department tile.
 *
 * The API does not supply category imagery, so the visual cue is a stable icon chosen from the
 * department slug. Slugs are presentation-only here: no backend identifier is invented or used.
 */
const CATEGORY_ICONS: Record<string, LucideIcon> = {
  electronics: Tv,
  fashion: Shirt,
  "home-living": Home,
  "mens-clothing": Shirt,
  "womens-clothing": Shirt,
  accessories: ShoppingBag,
  "mobile-phones": Smartphone,
  wearables: Watch,
  "headphones-audio": Headphones,
  kitchen: Home,
};

export function CategoryTile({ category }: { category: Category }) {
  const Icon = CATEGORY_ICONS[category.slug] ?? Tag;
  const countLabel = `${category.productCount} ${category.productCount === 1 ? "product" : "products"}`;

  return (
    <Link
      href={`/categories/${category.slug}`}
      className="mp-card mp-card-hover mp-category-tile"
      aria-label={`${category.name}, ${countLabel}`}
    >
      {category.imageUrl ? (
        <ProductPhoto
          src={category.imageUrl}
          alt=""
          width={160}
          height={160}
          style={{ width: "2.75rem", height: "2.75rem", flex: "none" }}
        />
      ) : (
        <span className="mp-category-icon" aria-hidden>
          <Icon size={20} />
        </span>
      )}

      <span className="mp-category-text">
        <span className="mp-category-name mp-truncate">{category.name}</span>
        <small className="mp-category-count">{countLabel}</small>
      </span>

      <ArrowRight size={16} aria-hidden className="mp-category-arrow" />
    </Link>
  );
}
