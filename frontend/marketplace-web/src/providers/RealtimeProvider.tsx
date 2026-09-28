"use client";

/**
 * The real-time connection, and the reads each event invalidates.
 *
 * The API publishes eleven events: an order created, an order moved, a payment settled, a refund
 * changed, a notification raised, stock running low, a seller's status changed, a review posted,
 * the platform's figures moved, the cart changed. All of them were being published to a hub nobody
 * was connected to, so a customer placed an order and watched a stale list until they refreshed.
 *
 * A handler invalidates exactly the reads its event affects, and no others. Invalidating
 * everything on every event is simpler and wrong in a way people notice: a page that refetches
 * while somebody is typing in a form loses what they typed.
 *
 * Joining an order's group is the server's decision, not the client's — the connection is placed
 * in the user's own group from the token, and a client asking to join somebody else's is refused.
 */

import { createContext, useCallback, useContext, useEffect, useMemo, type ReactNode } from "react";
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";

import { queryKeys } from "@/lib/queryKeys";
import { useAuthStore } from "@/store/authStore";

/** The method names the API sends. Kept as one list so a typo cannot silently drop an event. */
const EVENTS = {
  notificationCreated: "NotificationCreated",
  orderCreated: "OrderCreated",
  orderUpdated: "OrderUpdated",
  orderStatusChanged: "OrderStatusChanged",
  paymentUpdated: "PaymentUpdated",
  refundUpdated: "RefundUpdated",
  inventoryLow: "InventoryLow",
  sellerStatusChanged: "SellerStatusChanged",
  newReview: "NewReview",
  platformMetricsUpdated: "PlatformMetricsUpdated",
  cartUpdated: "CartUpdated",
} as const;

function hubUrl(): string {
  const configured = process.env.NEXT_PUBLIC_SIGNALR_URL;

  if (configured) {
    return configured;
  }

  // Derived from the API's address rather than set separately, so the two halves cannot end up
  // pointing at different ports and leave the real-time connection quietly dead.
  const api = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

  return `${api.replace(/\/+$/, "")}/hubs/marketplace`;
}

interface RealtimeContextValue {
  /** Subscribes to one order's updates, for a page watching a parcel move. */
  watchOrder: (orderId: string) => Promise<void>;
}

const RealtimeContext = createContext<RealtimeContextValue>({ watchOrder: async () => {} });

/**
 * The live connection, held here rather than in state.
 *
 * A connection is not rendered, so putting it in state would mean a render purely to publish an
 * object nobody draws. Pages reach it through a callback, which is called after render — never
 * during it. One connection at a time: the app has one hub per signed-in session.
 */
let activeHub: HubConnection | null = null;

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const accessToken = useAuthStore(state => state.accessToken);
  const isAuthenticated = useAuthStore(state => state.user !== null);

  useEffect(() => {
    if (!isAuthenticated || !accessToken) {
      // Signed out: there is nothing to receive, and a connection that outlived the session would
      // keep asking with a token that no longer works. The cleanup below has already closed it.
      return;
    }

    const hub = new HubConnectionBuilder()
      .withUrl(hubUrl(), { accessTokenFactory: () => accessToken })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    activeHub = hub;


    // Each handler invalidates only what its event makes untrue. A shopper watching an order sees
    // it move without touching reload; a seller sees stock fall; an admin sees the figures move.
    const handlers: [string, () => void][] = [
      [
        EVENTS.notificationCreated,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.notifications.all });
        },
      ],
      [
        EVENTS.orderCreated,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.orders.all });
          void queryClient.invalidateQueries({ queryKey: queryKeys.cart.all });
          void queryClient.invalidateQueries({ queryKey: queryKeys.seller.orders({})[0] });
        },
      ],
      [
        EVENTS.orderUpdated,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.orders.all });
          void queryClient.invalidateQueries({ queryKey: queryKeys.seller.orders({})[0] });
        },
      ],
      [
        EVENTS.orderStatusChanged,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.orders.all });
          void queryClient.invalidateQueries({ queryKey: queryKeys.seller.orders({})[0] });
        },
      ],
      [
        EVENTS.paymentUpdated,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.payments.all });
          void queryClient.invalidateQueries({ queryKey: queryKeys.orders.all });
        },
      ],
      [
        EVENTS.refundUpdated,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.orders.all });
          void queryClient.invalidateQueries({ queryKey: queryKeys.admin.all });
        },
      ],
      [
        EVENTS.inventoryLow,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.seller.inventory(1) });
          void queryClient.invalidateQueries({ queryKey: queryKeys.products.all });
        },
      ],
      [
        EVENTS.sellerStatusChanged,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.admin.sellers({})[0] });
          void queryClient.invalidateQueries({ queryKey: queryKeys.seller.all });
        },
      ],
      [
        EVENTS.newReview,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.seller.reviews({})[0] });
          void queryClient.invalidateQueries({ queryKey: queryKeys.products.all });
        },
      ],
      [
        EVENTS.platformMetricsUpdated,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.admin.all });
        },
      ],
      [
        EVENTS.cartUpdated,
        () => {
          void queryClient.invalidateQueries({ queryKey: queryKeys.cart.all });
        },
      ],
    ];

    for (const [method, onEvent] of handlers) {
      hub.on(method, onEvent);
    }

    hub.onreconnected(async () => {
      // The server put this connection in groups from the token. A reconnect gets a new id, so
      // the groups have to be asked for again or updates arrive to nobody.
      try {
        const state = await hub.invoke("Ping");
        void state;
      } catch {
        // A failed probe is not worth surfacing: the reconnect backoff is already handling it.
      }
    });

    void hub.start().catch(() => {
      // Offline, or the API is not up yet. Automatic reconnection retries, and nothing the user is
      // doing depends on this working: every read is a normal request first.
    });

    return () => {
      for (const [method] of handlers) {
        hub.off(method);
      }

      void hub.stop();
      activeHub = null;
    };
  }, [accessToken, isAuthenticated, queryClient]);

  const watchOrder = useCallback(async (orderId: string) => {
    if (activeHub?.state !== HubConnectionState.Connected) {
      return;
    }

    await activeHub.invoke("JoinOrder", orderId).catch(() => {
      // Not being in the group costs live updates for this order, not the order itself: the page
      // still reads it over HTTP.
    });
  }, []);

  const value = useMemo<RealtimeContextValue>(() => ({ watchOrder }), [watchOrder]);

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

export function useRealtime(): RealtimeContextValue {
  return useContext(RealtimeContext);
}
