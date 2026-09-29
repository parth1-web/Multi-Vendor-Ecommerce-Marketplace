import { PackageCheck, ShoppingBag, Store, TicketPercent, type LucideIcon } from "lucide-react";

export interface TrustItem {
  icon: LucideIcon;
  title: string;
  body: string;
}

/**
 * The marketplace guarantees that are actually backed by the product.
 *
 * Every line maps to working behavior: real storefronts keep their own catalogues, the basket
 * can span sellers before one checkout, discount codes are quoted before payment, and orders
 * remain visible by store and status. Nothing here promises delivery speed or a return policy
 * the backend does not expose.
 */
export const MARKETPLACE_TRUST_ITEMS: TrustItem[] = [
  {
    icon: Store,
    title: "Independent stores",
    body: "Every listing belongs to a real storefront with its own catalogue.",
  },
  {
    icon: ShoppingBag,
    title: "One basket, one checkout",
    body: "Shop several sellers without paying at every till.",
  },
  {
    icon: TicketPercent,
    title: "Codes honored upfront",
    body: "Discount eligibility is quoted before payment, not after.",
  },
  {
    icon: PackageCheck,
    title: "Orders stay visible",
    body: "History remains grouped by store and order status.",
  },
];

export function TrustStrip({ items = MARKETPLACE_TRUST_ITEMS }: { items?: TrustItem[] }) {
  return (
    <section aria-label="Why shop on this marketplace" className="mp-trust">
      {items.map((item) => (
        <div key={item.title} className="mp-trust-item">
          <span className="mp-trust-icon" aria-hidden>
            <item.icon size={18} />
          </span>
          <div>
            <p className="mp-trust-title">{item.title}</p>
            <p className="mp-trust-body">{item.body}</p>
          </div>
        </div>
      ))}
    </section>
  );
}
