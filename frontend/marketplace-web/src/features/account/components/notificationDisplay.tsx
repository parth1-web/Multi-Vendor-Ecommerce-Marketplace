import {
  Bell,
  BellRing,
  CreditCard,
  Package,
  RotateCcw,
  Star,
  Store,
  Tag,
  Wallet,
  type LucideIcon,
} from "lucide-react";

import type { NotificationType } from "@/types/account";

/**
 * Everything visual about a notification type, in one place.
 *
 * The backend owns nineteen types; the frontend groups them into eight icons and labels. A type
 * the backend adds tomorrow falls back to a neutral bell rather than rendering nothing, and a
 * type nobody sends costs nothing. Icons are decoration — the label beside each one carries the
 * meaning, so nothing here depends on telling icons apart.
 */
export const NOTIFICATION_TYPES: NotificationType[] = [
  "OrderCreated",
  "OrderConfirmed",
  "OrderShipped",
  "OrderDelivered",
  "OrderCancelled",
  "PaymentSuccessful",
  "PaymentFailed",
  "RefundRequested",
  "RefundApproved",
  "RefundRejected",
  "SellerApproved",
  "SellerRejected",
  "SellerSuspended",
  "ProductApproved",
  "ProductRejected",
  "LowStock",
  "NewReview",
  "SystemNotification",
  "PayoutProcessed",
  "SystemAlert",
];

export function notificationIcon(type: NotificationType): LucideIcon {
  switch (type) {
    case "OrderCreated":
    case "OrderConfirmed":
    case "OrderShipped":
    case "OrderDelivered":
    case "OrderCancelled":
    case "LowStock":
      return Package;
    case "PaymentSuccessful":
    case "PaymentFailed":
      return CreditCard;
    case "RefundRequested":
    case "RefundApproved":
    case "RefundRejected":
      return RotateCcw;
    case "ProductApproved":
    case "ProductRejected":
      return Tag;
    case "SellerApproved":
    case "SellerRejected":
    case "SellerSuspended":
      return Store;
    case "NewReview":
      return Star;
    case "PayoutProcessed":
      return Wallet;
    case "SystemNotification":
    case "SystemAlert":
      return BellRing;
    default:
      return Bell;
  }
}

export function notificationKind(type: NotificationType): string {
  switch (type) {
    case "OrderCreated":
    case "OrderConfirmed":
    case "OrderShipped":
    case "OrderDelivered":
    case "OrderCancelled":
      return "Order";
    case "PaymentSuccessful":
    case "PaymentFailed":
      return "Payment";
    case "RefundRequested":
    case "RefundApproved":
    case "RefundRejected":
      return "Refund";
    case "ProductApproved":
    case "ProductRejected":
      return "Your product";
    case "SellerApproved":
    case "SellerRejected":
    case "SellerSuspended":
      return "Seller account";
    case "LowStock":
      return "Stock";
    case "NewReview":
      return "Review";
    case "PayoutProcessed":
      return "Payout";
    default:
      return "Update";
  }
}

/**
 * Turns a backend notification link into a frontend route, or null when it cannot be honored.
 *
 * Only same-site paths are ever returned: anything else is untrusted input, not a destination.
 * Order links carry order *numbers*, but the order route takes an id — so those become an order
 * search for the number, which the backend matches exactly. A dead button that 404s would be
 * worse than no button, and a fabricated id worse still.
 */
export function resolveNotificationHref(link: string | null): string | null {
  if (!link || !link.startsWith("/")) {
    return null;
  }

  const orderNumber = /^\/orders\/([^/?#]+)([?#].*)?$/.exec(link);

  if (orderNumber && !isGuid(orderNumber[1])) {
    return `/orders?search=${encodeURIComponent(orderNumber[1])}`;
  }

  return link;
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}
