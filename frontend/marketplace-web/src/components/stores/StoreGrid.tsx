import { StoreCard } from "@/components/stores/StoreCard";
import type { StoreDirectoryEntry } from "@/types/store";

/**
 * The responsive grid behind both the store directory and homepage rails.
 *
 * One column on a phone, up to four across on a desktop, without horizontal scrolling. Cards are
 * list items because a storefront directory is a list of destinations, not decoration.
 */
export function StoreGrid({ stores }: { stores: StoreDirectoryEntry[] }) {
  return (
    <div className="mp-store-grid" role="list">
      {stores.map((store) => (
        <div key={store.storeId} role="listitem" style={{ minWidth: 0 }}>
          <StoreCard store={store} />
        </div>
      ))}
    </div>
  );
}
