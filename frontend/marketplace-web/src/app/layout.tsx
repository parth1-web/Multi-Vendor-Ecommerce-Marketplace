import type { Metadata, Viewport } from "next";
import { Inter, JetBrains_Mono, Plus_Jakarta_Sans, Sora } from "next/font/google";

import { AppProviders } from "@/providers/AppProviders";

import "../styles/globals.css";

/**
 * The fonts the design system names, loaded through next/font so they are self-hosted,
 * subset and preloaded. Sora and Plus Jakarta Sans are display faces used sparingly; Inter is
 * the workhorse for body copy and JetBrains Mono is for codes and references.
 */
const sans = Inter({ subsets: ["latin"], variable: "--font-inter", display: "swap" });
const display = Plus_Jakarta_Sans({ subsets: ["latin"], variable: "--font-jakarta", display: "swap" });
const heading = Sora({ subsets: ["latin"], variable: "--font-sora", display: "swap" });
const mono = JetBrains_Mono({ subsets: ["latin"], variable: "--font-mono", display: "swap" });

export const metadata: Metadata = {
  metadataBase: new URL(process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000"),
  title: {
    default: "Marketplace — multi-vendor shopping",
    template: "%s | Marketplace",
  },
  description:
    "A multi-vendor marketplace: browse products from independent sellers, track orders, and manage a store from one place.",
  openGraph: {
    type: "website",
    siteName: "Marketplace",
  },
  robots: { index: true, follow: true },
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#f6f7fb" },
    { media: "(prefers-color-scheme: dark)", color: "#0b0e1a" },
  ],
};

/**
 * The theme, applied before the first paint.
 *
 * The store is the source of truth, but a store is read after hydration, and until then the page
 * has no data-theme and every dark token is unused — a full white flash on every load for anyone
 * who chose dark. This runs in the document head, before anything is painted, and is the only
 * place a theme is set from storage. It is deliberately tiny and has no framework in it, because
 * it runs before the framework exists.
 */
const themeScript = `
(function () {
  try {
    var stored = localStorage.getItem('mp-theme');
    var theme = stored ? JSON.parse(stored).state.theme : null;
    if (theme !== 'light' && theme !== 'dark') {
      theme = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    document.documentElement.dataset.theme = theme;
  } catch (e) {
    document.documentElement.dataset.theme = 'light';
  }
})();
`.trim();

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    // suppressHydrationWarning: the theme is applied from a persisted store, so the server cannot
    // know it. The script above sets it before paint; this keeps React from objecting to the
    // attribute the server did not render.
    <html lang="en" suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body className={`${sans.variable} ${display.variable} ${heading.variable} ${mono.variable}`}>
        <AppProviders>{children}</AppProviders>
      </body>
    </html>
  );
}
