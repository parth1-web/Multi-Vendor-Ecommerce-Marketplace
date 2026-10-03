/** The record to read when a figure looks wrong: who changed what, and when. */

import type { Metadata } from "next";

import { AdminAuditLogs } from "@/features/admin/components/AdminAuditLogs";

export const metadata: Metadata = {
  title: "Audit log",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Audit log</h1>
          <p className="mp-page-subtitle">
            Every consequential action, with who did it and what it changed. Filter by action, by date, or by one target.
          </p>
        </div>
      </div>

      <AdminAuditLogs />
    </div>
  );
}