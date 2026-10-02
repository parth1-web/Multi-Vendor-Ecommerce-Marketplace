/** Every payment on the marketplace, and the gateway's own record of what happened to it. */

import type { Metadata } from "next";

import { AdminPayments } from "@/features/admin/components/AdminPayments";

export const metadata: Metadata = {
  title: "Payments",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Payments</h1>
          <p className="mp-page-subtitle">
            Every payment the marketplace has taken, and what the gateway reported back.
          </p>
        </div>
      </div>

      <AdminPayments />
    </div>
  );
}