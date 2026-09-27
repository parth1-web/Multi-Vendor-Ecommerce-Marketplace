/** Notifications: order updates, payments, refunds and seller news. */

import type { Metadata } from "next";

import { NotificationsPage } from "@/features/account/components/NotificationList";

export const metadata: Metadata = {
  title: "Notifications",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Notifications</h1>
          <p className="mp-page-subtitle">Everything the marketplace has told you, newest first.</p>
        </div>
      </div>

      <NotificationsPage />
    </div>
  );
}
