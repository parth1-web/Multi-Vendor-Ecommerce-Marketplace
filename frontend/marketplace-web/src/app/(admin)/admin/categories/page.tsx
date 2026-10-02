/** The shape of the catalogue, and what may be removed from it. */

import type { Metadata } from "next";

import { AdminCategories } from "@/features/admin/components/AdminCategories";

export const metadata: Metadata = {
  title: "Categories",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Categories</h1>
          <p className="mp-page-subtitle">
            The shape of the catalogue. A category with products or children cannot be deleted.
          </p>
        </div>
      </div>

      <AdminCategories />
    </div>
  );
}