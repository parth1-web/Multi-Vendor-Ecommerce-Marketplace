/** Every review on the marketplace, and the two decisions a moderator has about one. */

import type { Metadata } from "next";

import { AdminReviews } from "@/features/admin/components/AdminReviews";

export const metadata: Metadata = {
  title: "Reviews",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Reviews</h1>
          <p className="mp-page-subtitle">
            Every review a shopper has written, hidden ones included. Hiding one takes it off the product page and keeps
            the words; restoring it puts it back.
          </p>
        </div>
      </div>

      <AdminReviews />
    </div>
  );
}