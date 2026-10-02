/** Stock across every store, which is only visible to an administrator. */

import type { Metadata } from "next";

import { AdminInventory } from "@/features/admin/components/AdminInventory";

export const metadata: Metadata = {
  title: "Stock",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Stock</h1>
          <p className="mp-page-subtitle">
            Every store&apos;s stock in one list, and what each adjustment did to it.
          </p>
        </div>
      </div>

      <AdminInventory />
    </div>
  );
}