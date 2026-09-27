"use client";

/** The saved-items list, and the one question a heart on a card needs answered. */

import { useQuery } from "@tanstack/react-query";

import { wishlistApi } from "@/features/account/api/accountApi";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";

import type { WishlistItem } from "@/types/account";

/** The saved items themselves, for the wishlist page. */
export function useWishlist() {
  const { isAuthenticated, isHydrating } = useAuth();

  return useQuery<WishlistItem[]>({
    queryKey: queryKeys.wishlist.list(),
    queryFn: () => wishlistApi.list(),
    enabled: isAuthenticated && !isHydrating,
  });
}

/**
 * The ids of the saved items, as a set, for a grid of cards.
 *
 * A grid of forty cards cannot each ask this question, so it is asked once here and the answer
 * passed down. Without it every heart renders as unsaved whatever is in the list, which is worse
 * than not showing a heart at all: the shopper saves something, comes back, and is told it is not
 * saved.
 */
export function useSavedProductIds(): ReadonlySet<string> {
  const { data } = useWishlist();

  return new Set((data ?? []).map(item => item.productId));
}
