/** Seller accounts: approving, refusing and suspending, each with a reason on the record. */

import { AdminSellers } from "@/features/admin/components/AdminPanels";

export default function SellersPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Sellers</h1>
          <p className="mp-page-subtitle">Who is selling, and what you decided about each of them.</p>
        </div>
      </div>

      <AdminSellers />
    </div>
  );
}