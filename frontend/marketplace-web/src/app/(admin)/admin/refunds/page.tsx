/** Every refund on the marketplace, and the one decision that moves money back. */

import type { Metadata } from "next";

import { AdminRefunds } from "@/features/admin/components/AdminRefunds";

export const metadata: Metadata = {
  title: "Refunds",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Refunds</h1>
          <p className="mp-page-subtitle">
            What shoppers have asked for, and what approving it actually does.
          </p>
        </div>
      </div>

      <AdminRefunds />
    </div>
  );
}