/** The storefront shell: header, content, footer. A server component. */

import type { ReactNode } from "react";

import { CategoryNav } from "@/components/layout/CategoryNav";
import { PublicFooter } from "@/components/layout/PublicFooter";
import { PublicHeader } from "@/components/layout/PublicHeader";
import { SessionRedirect } from "@/components/layout/SessionRedirect";
import { UtilityBar } from "@/components/layout/UtilityBar";

export default function PublicLayout({ children }: { children: ReactNode }) {
  return (
    <div style={{ minHeight: "100dvh", display: "flex", flexDirection: "column" }}>
      <a className="mp-skip-link" href="#main-content">
        Skip to main content
      </a>
      <SessionRedirect />
      <UtilityBar />
      <PublicHeader />
      <CategoryNav />
      <main id="main-content" style={{ flex: 1 }}>
        {children}
      </main>
      <PublicFooter />
    </div>
  );
}
