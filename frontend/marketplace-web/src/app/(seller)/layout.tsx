/** The seller area: a different chrome from the shop, and gated before anything renders. */

import type { Metadata } from "next";

import { SellerLayout } from "@/features/seller/components/SellerLayout";

export const metadata: Metadata = {
  title: "Seller dashboard",
  robots: { index: false, follow: false },
};

export default function SellerLayoutPage({ children }: { children: React.ReactNode }) {
  return <SellerLayout>{children}</SellerLayout>;
}
