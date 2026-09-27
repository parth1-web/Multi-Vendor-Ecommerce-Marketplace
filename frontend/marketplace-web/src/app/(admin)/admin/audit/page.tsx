/** Everything that changed something, and who changed it. */

import { AdminAuditLog } from "@/features/admin/components/AdminPanels";

export default function AuditPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Audit log</h1>
          <p className="mp-page-subtitle">The record to read when a figure looks wrong.</p>
        </div>
      </div>

      <AdminAuditLog />
    </div>
  );
}