import Link from "next/link";
import { TicketPercent } from "lucide-react";

import { serverGetQuietly } from "@/lib/serverApi";
import type { PublicCoupon } from "@/features/promotions/api/publicCouponApi";

/**
 * The narrow strip above the header.
 *
 * It only says things the product can prove: independent storefronts, one basket across them,
 * and a live discount code when the API has one running. Generic shipping or return promises
 * are deliberately absent because the backend exposes neither as a global storefront promise.
 */
export async function UtilityBar() {
  const coupons = await serverGetQuietly<PublicCoupon[]>("/api/coupons/public", 300);
  const coupon = coupons?.find((candidate) => candidate.code && candidate.discountLabel) ?? null;

  return (
    <div className="mp-utility-bar">
      <div className="mp-page mp-utility-inner">
        <span className="mp-utility-note">Independent sellers · One checkout</span>

        {coupon ? (
          <Link className="mp-utility-promo" href="/deals" aria-label={`Deal of the moment: ${coupon.discountLabel}`}>
            <TicketPercent size={14} aria-hidden />
            <span className="mp-truncate">
              Use code <strong>{coupon.code}</strong>
              <span aria-hidden> · </span>
              {coupon.discountLabel}
            </span>
          </Link>
        ) : (
          <span className="mp-utility-note">Discount codes accepted at checkout</span>
        )}

        <Link className="mp-utility-link" href="/register">
          Sell on Marketplace
        </Link>
      </div>
    </div>
  );
}
