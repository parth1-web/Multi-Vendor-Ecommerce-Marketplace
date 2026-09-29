/**
 * The heart on a product card and detail page.
 *
 * A leaf client component, because the card around it is a server component and only this needs
 * a handler. It asks whether the product is already saved rather than assuming it is not: a grid
 * of forty cards cannot each fetch the wishlist, so the state is read once at the shell and
 * passed down as a set of ids.
 */

"use client";

import { Heart } from "lucide-react";
import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { wishlistApi } from "@/features/account/api/accountApi";
import { useSavedProductIds } from "@/features/account/api/useWishlist";
import { errorMessage } from "@/lib/errors";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";

export function WishlistButton({
  productId,
  saved,
  className,
  label = "Save for later",
}: {
  productId: string;
  saved: boolean;
  className?: string;
  label?: string;
}) {
  const queryClient = useQueryClient();
  const { isAuthenticated } = useAuth();
  const [failed, setFailed] = useState<string | null>(null);
  // Homepage cards render on the server without a saved prop, so reconcile the prop with the
  // live wishlist. Guests have no list, and one cached list serves the whole grid.
  const savedIds = useSavedProductIds();
  const isSaved = saved || savedIds.has(productId);

  const toggle = useMutation({
    // Both directions end in the same state — the item is either saved or not — so the
    // mutation is typed as that rather than as whichever call the branch happened to make.
    mutationFn: async (): Promise<void> => {
      if (isSaved) {
        await wishlistApi.remove(productId);
      } else {
        await wishlistApi.add(productId);
      }
    },
    onMutate: () => setFailed(null),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.wishlist.all }),
    onError: error => setFailed(errorMessage(error, "We could not update your saved items.")),
  });

  // Signed out, the heart is still shown: tapping it says why nothing happened, rather than
  // leaving a control that looks broken. The reason is shown as well as the link, because a
  // button that silently does nothing is worse than one that explains itself.
  const signInUrl = `/login?returnUrl=${encodeURIComponent(typeof window === "undefined" ? "/" : window.location.pathname)}`;

  return (
    <div className={className}>
      <button
        type="button"
        className="btn btn-sm"
        onClick={() => (isAuthenticated ? toggle.mutate() : setFailed("Saving items needs an account."))}
        disabled={toggle.isPending}
        aria-busy={toggle.isPending}
        aria-pressed={isSaved}
        aria-label={toggle.isPending ? "Saving..." : isSaved ? `Remove from saved items` : label}
        title={isAuthenticated ? (isSaved ? "Remove from saved items" : label) : "Sign in to save items"}
        style={{
          color: isSaved ? "var(--danger)" : "var(--text-subtle)",
          lineHeight: 1,
        }}
      >
        <Heart size={16} aria-hidden fill={isSaved ? "currentColor" : "none"} />
      </button>


      {failed ? (
        <p role="alert" style={{ margin: "0.25rem 0 0", fontSize: "var(--fs-xs)", color: "var(--danger)" }}>
          {failed}{" "}
          {!isAuthenticated ? (
            <a href={signInUrl}>Sign in</a>
          ) : null}
        </p>
      ) : null}
    </div>
  );
}
