/** The storefront shell: header, content, footer. A server component. */

import type { ReactNode } from "react";

import { PublicFooter } from "@/components/layout/PublicFooter";
import { PublicHeader } from "@/components/layout/PublicHeader";
import { SessionRedirect } from "@/components/layout/SessionRedirect";

export default function PublicLayout({ children }: { children: ReactNode }) {
  return (
    <div style={{ minHeight: "100dvh", display: "flex", flexDirection: "column" }}>
      <SessionRedirect />
      <PublicHeader />
      <main style={{ flex: 1 }}>{children}</main>
      <PublicFooter />
    </div>
  );
}
