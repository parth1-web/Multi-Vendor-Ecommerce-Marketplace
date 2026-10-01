/** Notifications: order updates, payments, refunds and seller news. */

import type { Metadata } from "next";
import { Suspense } from "react";

import { Breadcrumbs } from "@/components/navigation/Breadcrumbs";
import { NotificationsPage } from "@/features/account/components/NotificationList";

export const metadata: Metadata = {
  title: "Notifications",
  description: "Order updates, payments, refunds, and account activity.",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <Breadcrumbs trail={[{ name: "Home", href: "/" }, { name: "Notifications" }]} />

      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Notifications</h1>
          <p className="mp-page-subtitle">Everything the marketplace has told you, newest first.</p>
        </div>
      </div>

      {/* The list reads the address bar, and address-bar readers suspend while navigating. */}
      <Suspense
        fallback={<div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} aria-hidden />}
      >
        <NotificationsPage />
      </Suspense>
    </div>
  );
}
