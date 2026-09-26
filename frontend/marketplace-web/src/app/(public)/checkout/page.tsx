/**
 * The checkout page.
 *
 * A client page behind a session: everything it shows is the signed-in person's basket, their
 * addresses and their order, none of which belongs in a cached or crawlable page.
 */

"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { CheckoutWizard } from "@/features/orders/components/CheckoutWizard";
import { useAuth } from "@/providers/AuthProvider";

export default function CheckoutPage() {
  const router = useRouter();
  const { isAuthenticated, isHydrating } = useAuth();

  // Someone with no session is sent to sign in with this page as the destination, so they land
  // back here with their basket intact. The check waits for hydration because on a cold load
  // there is no session yet and redirecting immediately would bounce everyone.
  useEffect(() => {
    if (!isHydrating && !isAuthenticated) {
      router.replace("/login?returnUrl=%2Fcheckout");
    }
  }, [isAuthenticated, isHydrating, router]);

  if (!isAuthenticated) {
    return (
      <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
        <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)" }} />
      </div>
    );
  }

  return (
    <div className="mp-page" style={{ paddingBlock: "var(--space-5)" }}>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Checkout</h1>
          <p className="mp-page-subtitle">
            Three steps: where it goes, how you are paying, and a last look before it is placed.{" "}
            <Link href="/cart">Back to your basket</Link>
          </p>
        </div>
      </div>

      <CheckoutWizard />
    </div>
  );
}
