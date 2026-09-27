/** The account page: who you are, and how to change it. */

import type { Metadata } from "next";

import { AccountPage } from "@/features/account/components/AccountPanel";

export const metadata: Metadata = {
  title: "Your account",
  robots: { index: false, follow: false },
};

export default function Page() {
  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Your account</h1>
          <p className="mp-page-subtitle">Your details, and the password you sign in with.</p>
        </div>
      </div>

      <AccountPage />
    </div>
  );
}
