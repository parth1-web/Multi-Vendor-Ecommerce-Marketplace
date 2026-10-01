/**
 * The wishlist.
 *
 * A list of things somebody meant to come back for, so the useful things on it are the price as
 * it is now, whether it is still in stock, and a way to get rid of an entry without hunting.
 */

"use client";

import Link from "next/link";
import { Trash2 } from "lucide-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { PriceDisplay } from "@/components/shared/PriceDisplay";
import { RatingStars } from "@/components/shared/RatingStars";
import { RequireAuth } from "@/features/account/components/RequireAuth";
import { useWishlist } from "@/features/account/api/useWishlist";
import { wishlistApi } from "@/features/account/api/accountApi";
import { formatDate } from "@/lib/format";
import { errorMessage } from "@/lib/errors";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { WishlistItem } from "@/types/account";

function WishlistView() {
  const queryClient = useQueryClient();
  const { push } = useToast();

  // The same query the hearts read, so a heart turned on a product card has already filled this
  // page in. Two keys for one list is two answers to the same question.
  const wishlist = useWishlist();

  const remove = useMutation({
    mutationFn: (productId: string) => wishlistApi.remove(productId),
    onSuccess: (_data, productId) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.wishlist.all });
      const name = wishlist.data?.find((item) => item.productId === productId)?.name;
      push({ tone: "success", title: name ? `Removed ${name} from your saved items` : "Removed from your saved items" });
    },
    onError: (failure) =>
      push({ tone: "danger", title: "Could not remove the saved item", body: errorMessage(failure) }),
  });

  const clear = useMutation({
    mutationFn: () => wishlistApi.clear(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.wishlist.all });
      push({ tone: "success", title: "Wishlist cleared" });
    },
    onError: (failure) =>
      push({ tone: "danger", title: "Could not clear the wishlist", body: errorMessage(failure) }),
  });

  if (wishlist.isPending) {
    return (
      <div className="row g-3">
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="col-12 col-md-6 col-xl-4">
            <div className="mp-skeleton" style={{ height: "10rem", borderRadius: "var(--radius)" }} />
          </div>
        ))}
      </div>
    );
  }

  if (wishlist.isError) {
    return <ErrorState message="We could not load your saved items." />;
  }

  if (wishlist.data.length === 0) {
    return (
      <EmptyState
        title="Nothing saved yet"
        body="The heart on a product saves it here, so you can come back to it without searching again."
        action={
          <Link href="/products" className="btn btn-sm btn-primary">
            Browse the marketplace
          </Link>
        }
      />
    );
  }

  return (
    <div className="mp-stack">
      <div className="mp-spread">
        <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          {wishlist.data.length} {wishlist.data.length === 1 ? "item" : "items"} saved
        </p>
        <button
          type="button"
          className="btn btn-sm btn-outline-secondary"
          onClick={() => clear.mutate()}
          disabled={clear.isPending}
        >
          {clear.isPending ? "Clearing…" : "Clear the list"}
        </button>
      </div>

      <div className="row g-3">
        {wishlist.data.map((item, index) => (
          <div key={item.productId} className="col-12 col-md-6 col-xl-4">
            <WishlistCard
              item={item}
              busy={remove.isPending && remove.variables === item.productId}
              onRemove={() => remove.mutate(item.productId)}
              style={index === 0 ? undefined : undefined}
            />
          </div>
        ))}
      </div>
    </div>
  );
}

function WishlistCard({
  item,
  busy,
  onRemove,
}: {
  item: WishlistItem;
  busy: boolean;
  onRemove: () => void;
  style?: React.CSSProperties;
}) {
  return (
    <article className="mp-card" style={{ padding: "var(--space-3)", height: "100%" }}>
      <div className="d-flex" style={{ gap: "var(--space-3)" }}>
        {item.imageUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={item.imageUrl}
            alt={item.name}
            width={72}
            height={72}
            style={{ width: "4.5rem", height: "4.5rem", objectFit: "cover", borderRadius: "var(--radius-sm)", flex: "none" }}
          />
        ) : null}

        <div style={{ flex: 1, minWidth: 0 }}>
          <Link href={`/products/${item.slug}`} style={{ color: "var(--text)", fontWeight: 500, fontSize: "var(--fs-sm)" }}>
            {item.name}
          </Link>
          <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
            {item.storeName} · saved {formatDate(item.addedAt)}
          </p>

          <div className="d-flex align-items-center flex-wrap mt-1" style={{ gap: "var(--space-2)" }}>
            <RatingStars rating={item.ratingAverage} count={item.ratingCount} />
          </div>

          <PriceDisplay price={item.price} compareAtPrice={item.compareAtPrice} discountPercentage={item.discountPercentage} />

          {!item.isInStock ? (
            <p style={{ margin: "var(--space-1) 0 0", color: "var(--danger)", fontSize: "var(--fs-xs)" }}>Out of stock</p>
          ) : null}
        </div>

        <button
          type="button"
          className="btn btn-sm"
          onClick={onRemove}
          disabled={busy}
          aria-label={`Remove ${item.name} from your saved items`}
          style={{ color: "var(--text-subtle)", alignSelf: "flex-start" }}
        >
          <Trash2 size={16} aria-hidden />
        </button>
      </div>
    </article>
  );
}

export function WishlistPage() {
  return (
    <RequireAuth returnTo="/wishlist">
      <WishlistView />
    </RequireAuth>
  );
}
