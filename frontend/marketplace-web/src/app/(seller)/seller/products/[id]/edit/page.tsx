/** Edit a listing's own details. Images, variants and stock live on the product page. */

import type { Metadata } from "next";

import { ProductEditor } from "@/features/seller/components/ProductEditor";

export const metadata: Metadata = {
  title: "Edit a product",
  robots: { index: false, follow: false },
};

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;

  return <ProductEditor productId={id} />;
}
