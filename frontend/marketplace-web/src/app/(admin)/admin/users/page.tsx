/** Accounts on the marketplace, and the two changes that take effect immediately. */

import { AdminUsers } from "@/features/admin/components/AdminUsers";

export default function UsersPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Accounts</h1>
          <p className="mp-page-subtitle">Every account on the marketplace, and what it can do.</p>
        </div>
      </div>

      <AdminUsers />
    </div>
  );
}