/** The whole marketplace in one page: what came in, and what is waiting for a decision. */

import { AdminOverview } from "@/features/admin/components/AdminOverview";

export default function AdminPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Overview</h1>
          <p className="mp-page-subtitle">What the marketplace is doing, and what is waiting on you.</p>
        </div>
      </div>

      <AdminOverview />
    </div>
  );
}