/** The reviews of your products, and a chance to answer them. */

import type { Metadata } from "next";

import { SellerReviews } from "@/features/seller/components/SellerReviews";

export const metadata: Metadata = {
  title: "Your reviews",
  robots: { index: false, follow: false },
};

export default function Page() {
  return <SellerReviews />;
}
