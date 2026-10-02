/**
 * One vocabulary for a sub-order's status, and the steps it may take from where it is.
 *
 * Written once because a status is read in at least three places — the list, the detail page and
 * the dashboard — and three copies of a switch statement is three chances for the badge to say
 * something different from the button that moves it there. The wording is plain on purpose:
 * these are the states the API exposes, not a seller's private jargon for them.
 */

import type { Tone } from "@/lib/constants";
import type { SellerOrderStatus } from "@/types/seller";

/** Every status, in the order work happens. */
export const SELLER_ORDER_STATUSES: SellerOrderStatus[] = [
  "Pending",
  "Confirmed",
  "Processing",
  "Packed",
  "Shipped",
  "Delivered",
  "Completed",
  "Cancelled",
  "Returned",
];

const LABELS: Record<SellerOrderStatus, string> = {
  Pending: "Awaiting you",
  Confirmed: "Confirmed",
  Processing: "Being prepared",
  Packed: "Packed",
  Shipped: "Shipped",
  Delivered: "Delivered",
  Completed: "Completed",
  Cancelled: "Cancelled",
  Returned: "Returned",
};

export function orderStatusLabel(status: SellerOrderStatus): string {
  return LABELS[status] ?? status;
}

/**
 * The visual treatment. The badge also carries a dot and its label, so the tone is reinforcement
 * rather than the only thing carrying the meaning.
 */
export function orderStatusTone(status: SellerOrderStatus): Tone {
  switch (status) {
    case "Completed":
    case "Delivered":
      return "success";
    case "Cancelled":
    case "Returned":
      return "danger";
    case "Pending":
      return "warning";
    case "Confirmed":
    case "Processing":
    case "Packed":
    case "Shipped":
      return "info";
    default:
      return "info";
  }
}

export interface StatusStep {
  to: SellerOrderStatus;
  label: string;
  /** Shipping needs a carrier and a tracking number before the API will accept it. */
  requiresShipment?: boolean;
  /** Cancelling cannot be undone from the workspace, so it is confirmed rather than offered quietly. */
  destructive?: boolean;
  hint?: string;
}

/**
 * The steps available from the order's current status, mirroring the server's state machine.
 *
 * A step the API would refuse is not rendered disabled: it is not offered at all, because a
 * permanently greyed button invites the question "why" and answers nothing. Cancelling is legal
 * only before the parcel is on its way, which is exactly when a seller most wants it.
 */
export function nextOrderSteps(status: SellerOrderStatus): StatusStep[] {
  switch (status) {
    case "Pending":
      return [
        { to: "Confirmed", label: "Accept this order" },
        { to: "Cancelled", label: "Cancel this part of the order", destructive: true, hint: "Allowed until you ship." },
      ];
    case "Confirmed":
      return [
        { to: "Processing", label: "Start preparing" },
        { to: "Cancelled", label: "Cancel this part of the order", destructive: true, hint: "Allowed until you ship." },
      ];
    case "Processing":
      return [
        { to: "Packed", label: "Mark packed" },
        { to: "Cancelled", label: "Cancel this part of the order", destructive: true, hint: "Allowed until you ship." },
      ];
    case "Packed":
      return [{ to: "Shipped", label: "Mark shipped", requiresShipment: true }];
    case "Shipped":
      return [{ to: "Delivered", label: "Mark delivered" }];
    case "Delivered":
      return [
        { to: "Completed", label: "Close this order", hint: "Money is released once the order is complete." },
        { to: "Returned", label: "Record a return", destructive: true, hint: "Only after a delivered order is sent back." },
      ];
    default:
      // Cancelled, Returned and Completed are terminal: the API accepts no further step.
      return [];
  }
}

/** True when the API will not accept another step, so the page says so instead of offering one. */
export function isTerminalStatus(status: SellerOrderStatus): boolean {
  return nextOrderSteps(status).length === 0;
}