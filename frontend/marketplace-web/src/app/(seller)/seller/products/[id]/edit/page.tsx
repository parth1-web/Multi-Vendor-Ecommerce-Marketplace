/** One of your listings: its images, its variants, its stock, and whether it is live. */

import type { Metadata } from "next";

import { SellerProductDetail } from "@/features/seller/components/SellerProductDetail";

export const metadata: Metadata = {
  title: "Edit a product",
  robots: { index: false, follow: false },
};

export default function Page() {
  return <SellerProductDetail />;
}
