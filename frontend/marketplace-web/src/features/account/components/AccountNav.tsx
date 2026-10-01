"use client";

/**
 * The one navigation every account page shares.
 *
 * Five pages, one list of links: a copy per page is how one of them ends up pointing somewhere
 * that no longer exists, or forgetting which page is current. The row scrolls sideways on a
 * phone rather than collapsing into a menu, because every destination stays one tap away and
 * the current section stays visible without opening anything.
 */

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Bell, Heart, LayoutDashboard, LogOut, MapPin, Package, User } from "lucide-react";

import { useAuth } from "@/providers/AuthProvider";

const LINKS = [
  { href: "/dashboard", label: "Overview", icon: LayoutDashboard },
  { href: "/orders", label: "Orders", icon: Package },
  { href: "/wishlist", label: "Wishlist", icon: Heart },
  { href: "/addresses", label: "Addresses", icon: MapPin },
  { href: "/notifications", label: "Notifications", icon: Bell },
  { href: "/profile", label: "Profile", icon: User },
];

export function AccountNav() {
  const pathname = usePathname();
  const { signOut } = useAuth();

  return (
    <nav aria-label="Account sections" className="mp-seller-nav mb-4">
      {LINKS.map((link) => {
        const active = pathname === link.href;
        const Icon = link.icon;

        return (
          <Link
            key={link.href}
            href={link.href}
            className="mp-seller-link"
            aria-current={active ? "page" : undefined}
          >
            <Icon size={15} aria-hidden />
            {link.label}
          </Link>
        );
      })}

      <button
        type="button"
        className="mp-seller-link"
        style={{ color: "var(--text-subtle)", border: 0, background: "none", cursor: "pointer" }}
        onClick={() => void signOut()}
      >
        <LogOut size={15} aria-hidden />
        Sign out
      </button>
    </nav>
  );
}
