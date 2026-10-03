/** The four report families the API has, one at a time. */

import type { Metadata } from "next";

import { AdminReports } from "@/features/admin/components/AdminReports";

export const metadata: Metadata = {
  title: "Reports",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Reports</h1>
          <p className="mp-page-subtitle">
            Sales by period, sellers by revenue, stock across every store, and the commission the marketplace keeps.
          </p>
        </div>
      </div>

      <AdminReports />
    </div>
  );
}