/** The figures a marketplace is run on: sales, sellers and commission. */

import { AdminReports } from "@/features/admin/components/AdminPanels";

export default function ReportsPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Reports</h1>
          <p className="mp-page-subtitle">Sales, sellers and the commission the marketplace keeps.</p>
        </div>
      </div>

      <AdminReports />
    </div>
  );
}