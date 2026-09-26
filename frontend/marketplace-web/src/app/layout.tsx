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

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    // suppressHydrationWarning: the theme is applied by an effect from a persisted store, so
    // the server cannot know it. The mismatch is expected and harmless.
    <html lang="en" suppressHydrationWarning>
      <body className={`${sans.variable} ${display.variable} ${heading.variable} ${mono.variable}`}>
        <AppProviders>{children}</AppProviders>
      </body>
    </html>
  );
}
