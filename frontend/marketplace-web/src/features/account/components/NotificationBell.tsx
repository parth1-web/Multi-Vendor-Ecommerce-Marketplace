"use client";

/**
 * The header bell: an unread badge plus a preview of the newest notifications.
 *
 * Two different costs are kept apart on purpose. The badge reads the tiny unread-count endpoint
 * on every page, which is cheap enough to always have. The preview list only loads when the bell
 * is opened — fetching five full notification rows on every page view just in case would be the
 * expensive kind of dropdown.
 */

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell } from "lucide-react";

import { notificationApi } from "@/features/account/api/accountApi";
import {
  notificationIcon,
  notificationKind,
  resolveNotificationHref,
} from "@/features/account/components/notificationDisplay";
import { cx, formatRelative } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useAuth } from "@/providers/AuthProvider";

export function NotificationBell() {
  const router = useRouter();
  const pathname = usePathname();
  const { isAuthenticated } = useAuth();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);

  const unread = useQuery({
    queryKey: queryKeys.notifications.unreadCount(),
    queryFn: () => notificationApi.unreadCount(),
    enabled: isAuthenticated,
    staleTime: 30_000,
  });

  // The list is the expensive read, so it waits until somebody actually opens the bell. Opening
  // while signed out is impossible — the bell is a plain link then — so this never fires for a
  // guest.
  const recent = useQuery({
    queryKey: queryKeys.notifications.list({ page: 1, unreadOnly: false }),
    queryFn: () => notificationApi.list(1, 5),
    enabled: open && isAuthenticated,
    staleTime: 30_000,
  });

  const markRead = useMutation({
    mutationFn: (id: string) => notificationApi.markRead(id),
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications.all }),
  });

  const markAllRead = useMutation({
    mutationFn: () => notificationApi.markAllRead(),
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications.all }),
  });

  /* eslint-disable react-hooks/set-state-in-effect --
     The bell belongs to the page it was opened on, like every other header menu. Syncing open
     state to navigation is the documented exception here: there is no event to subscribe to,
     only a value that changed under the component. */
  useEffect(() => {
    setOpen(false);
  }, [pathname]);
  /* eslint-enable react-hooks/set-state-in-effect */

  useEffect(() => {
    if (!open) {
      return;
    }

    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
      }
    };

    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [open ]);

  const count = unread.data?.unreadCount ?? 0;

  if (!isAuthenticated) {
    return (
      <Link href="/notifications" className="btn btn-sm mp-icon-button" aria-label="Notifications" style={{ color: "var(--text-muted)" }}>
        <Bell size={18} />
      </Link>
    );
  }

  const openNotification = (id: string, isRead: boolean, href: string | null) => {
    if (!isRead) {
      markRead.mutate(id);
    }

    setOpen(false);
    router.push(href ?? "/notifications");
  };

  return (
    <div className="position-relative">
      <button
        type="button"
        className="btn btn-sm mp-icon-button"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        aria-haspopup="menu"
        aria-label={count > 0 ? `Notifications, ${count} unread` : "Notifications"}
        style={{ color: "var(--text-muted)" }}
      >
        <span style={{ position: "relative", display: "inline-flex" }}>
          <Bell size={18} aria-hidden />
          {count > 0 ? (
            <span className="mp-cart-count" aria-hidden>
              {count > 99 ? "99+" : count}
            </span>
          ) : null}
        </span>
      </button>

      {open ? (
        <div
          role="menu"
          aria-label="Recent notifications"
          className="mp-card-elevated position-absolute end-0 mt-2"
          style={{ width: "min(22rem, calc(100vw - 2rem))", padding: "var(--space-2)", zIndex: 1040 }}
        >
          <div className="mp-spread" style={{ padding: "var(--space-2)" }}>
            <span style={{ fontWeight: 600, fontSize: "var(--fs-sm)" }}>Notifications</span>
            {count > 0 ? (
              <button
                type="button"
                className="btn btn-sm btn-link p-0"
                style={{ fontSize: "var(--fs-xs)" }}
                disabled={markAllRead.isPending}
                onClick={() => markAllRead.mutate()}
              >
                {markAllRead.isPending ? "Marking…" : "Mark all read"}
              </button>
            ) : null}
          </div>

          {recent.isPending ? (
            <div className="mp-stack-sm" style={{ padding: "var(--space-2)" }} aria-hidden>
              {[0, 1, 2].map((index) => (
                <div key={index} className="mp-skeleton" style={{ height: "3rem", borderRadius: "var(--radius-sm)" }} />
              ))}
            </div>
          ) : recent.isError || !recent.data ? (
            <p style={{ margin: 0, padding: "var(--space-3) var(--space-2)", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              Could not load notifications.{" "}
              <Link href="/notifications" onClick={() => setOpen(false)}>
                View all
              </Link>
            </p>
          ) : recent.data.items.length === 0 ? (
            <p style={{ margin: 0, padding: "var(--space-3) var(--space-2)", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
              You&apos;re all caught up.
            </p>
          ) : (
            <ul className="list-unstyled mb-0">
              {recent.data.items.map((notification) => {
                const Icon = notificationIcon(notification.type);
                const href = resolveNotificationHref(notification.link);

                return (
                  <li key={notification.id}>
                    <button
                      type="button"
                      role="menuitem"
                      onClick={() => openNotification(notification.id, notification.isRead, href)}
                      className={cx("w-100 d-flex align-items-start", !notification.isRead && "mp-notification-unread")}
                      style={{
                        gap: "var(--space-2)",
                        padding: "var(--space-2)",
                        border: 0,
                        borderRadius: "var(--radius-sm)",
                        background: "none",
                        color: "var(--text)",
                        textAlign: "left",
                        cursor: "pointer",
                      }}
                    >
                      <span
                        className="mp-notification-dot"
                        aria-hidden
                        style={{
                          width: "0.45rem",
                          height: "0.45rem",
                          borderRadius: "50%",
                          marginTop: "0.4rem",
                          flex: "none",
                          backgroundColor: notification.isRead ? "transparent" : "var(--brand-600)",
                        }}
                      />
                      <span style={{ flex: 1, minWidth: 0 }}>
                        <span style={{ display: "block", fontSize: "var(--fs-sm)", fontWeight: notification.isRead ? 400 : 600 }} className="mp-truncate">
                          {notification.title}
                        </span>
                        <span style={{ display: "block", fontSize: "var(--fs-xs)", color: "var(--text-muted)" }}>
                          {notificationKind(notification.type)} · {formatRelative(notification.createdAt)}
                        </span>
                      </span>
                      <Icon size={16} aria-hidden style={{ color: "var(--text-subtle)", flex: "none", marginTop: "0.15rem" }} />
                    </button>
                  </li>
                );
              })}
            </ul>
          )}

          <Link
            href="/notifications"
            onClick={() => setOpen(false)}
            className="btn btn-sm btn-outline-secondary w-100 mt-2"
          >
            View all notifications
          </Link>
        </div>
      ) : null}
    </div>
  );
}
