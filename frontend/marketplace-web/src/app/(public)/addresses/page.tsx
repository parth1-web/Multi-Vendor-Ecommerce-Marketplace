/** The address book, and the place to change the default one. */

import type { Metadata } from "next";

import { AddressesPage } from "@/features/account/components/AddressBook";

export const metadata: Metadata = {
  title: "Your addresses",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Your addresses</h1>
          <p className="mp-page-subtitle">Where your orders go. Checkout uses the default one.</p>
        </div>
      </div>

      <AddressesPage />
    </div>
  );
}
