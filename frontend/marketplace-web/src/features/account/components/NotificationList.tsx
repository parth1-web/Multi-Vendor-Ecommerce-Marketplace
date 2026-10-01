/**
 * The notification list.
 *
 * Reading one is an action, not a page load: somebody triaging their list should not have to
 * wait for a round trip per row, so the row marks itself read and rolls back if the API
 * disagrees. Page, unread filter, and type filter live in the URL, so a filtered view is
 * shareable and survives refresh — the same contract the catalogue keeps.
 */

"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, X } from "lucide-react";

import { Pagination } from "@/components/navigation/Pagination";
import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { RequireAuth } from "@/features/account/components/RequireAuth";
import { notificationApi } from "@/features/account/api/accountApi";
import {
  NOTIFICATION_TYPES,
  notificationIcon,
  notificationKind,
  resolveNotificationHref,
} from "@/features/account/components/notificationDisplay";
import { cx, formatDate, formatRelative } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { NotificationPage, NotificationType } from "@/types/account";

const PAGE_SIZE = 20;

function readParams(searchParams: URLSearchParams): { page: number; unreadOnly: boolean; type?: NotificationType } {
  const page = Number(searchParams.get("page"));
  const type = searchParams.get("type");

  return {
    page: Number.isInteger(page) && page > 0 ? page : 1,
    unreadOnly: searchParams.get("unreadOnly") === "true",
    type: type && (NOTIFICATION_TYPES as string[]).includes(type) ? (type as NotificationType) : undefined,
  };
}

function NotificationList() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const { page, unreadOnly, type } = readParams(searchParams);

  const notifications = useQuery({
    queryKey: queryKeys.notifications.list({ page, unreadOnly, type }),
    queryFn: () => notificationApi.list(page, PAGE_SIZE, unreadOnly, type),
  });

  const navigate = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(searchParams.toString());

    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    next.delete("page");
    const queryString = next.toString();
    router.push(queryString ? `/notifications?${queryString}` : "/notifications");
  };

  const markRead = useMutation({
    mutationFn: (id: string) => notificationApi.markRead(id),
    onMutate: async id => {
      // Optimistic: the row is read the moment it is clicked, and the list is refetched
      // afterwards so the count agrees with the server either way.
      await queryClient.cancelQueries({ queryKey: queryKeys.notifications.all });
      const key = queryKeys.notifications.list({ page, unreadOnly, type });
      const previous = queryClient.getQueryData(key);

      queryClient.setQueryData(key, (current: NotificationPage | undefined) =>
        current
          ? {
              ...current,
              items: current.items.map(item => (item.id === id ? { ...item, isRead: true } : item)),
            }
          : current,
      );

      return { previous };
    },
    onError: (_error, _id, context) => {
      if (context?.previous) {
        queryClient.setQueryData(queryKeys.notifications.list({ page, unreadOnly, type }), context.previous);
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications.all }),
  });

  const markAllRead = useMutation({
    mutationFn: () => notificationApi.markAllRead(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications.all }),
  });

  const remove = useMutation({
    mutationFn: (id: string) => notificationApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications.all }),
  });

  const filtered = unreadOnly || type !== undefined;

  return (
    <div className="mp-stack">
      <form
        className="mp-card"
        style={{ padding: "var(--space-3) var(--space-4)" }}
        aria-label="Filter notifications"
        onSubmit={event => event.preventDefault()}
      >
        <div className="row g-2 align-items-end">
          <div className="col-12 col-md-4">
            <label htmlFor="notification-type" className="mp-metric-label">
              Type
            </label>
            <select
              id="notification-type"
              className="form-select form-select-sm"
              value={type ?? ""}
              onChange={event => navigate({ type: event.target.value || undefined })}
            >
              <option value="">All types</option>
              {NOTIFICATION_TYPES.map(value => (
                <option key={value} value={value}>
                  {notificationKind(value)}
                </option>
              ))}
            </select>
          </div>
          <div className="col-6 col-md-4">
            <label className="d-flex align-items-center" style={{ gap: "0.4rem", fontSize: "var(--fs-sm)", minHeight: "2.125rem" }}>
              <input
                type="checkbox"
                checked={unreadOnly}
                onChange={event => navigate({ unreadOnly: event.target.checked ? "true" : undefined })}
              />
              Unread only
            </label>
          </div>
          <div className="col-6 col-md-4 d-flex justify-content-end">
            <button
              type="button"
              className="btn btn-sm btn-outline-secondary"
              onClick={() => markAllRead.mutate()}
              disabled={markAllRead.isPending}
            >
              <Check size={14} aria-hidden className="me-1" />
              {markAllRead.isPending ? "Marking…" : "Mark everything read"}
            </button>
          </div>
        </div>
      </form>

      {notifications.isPending ? (
        <div className="mp-stack-sm" aria-hidden>
          {Array.from({ length: 4 }, (_, index) => (
            <div key={index} className="mp-card" style={{ padding: "var(--space-3)" }}>
              <div className="d-flex" style={{ gap: "var(--space-3)" }}>
                <div className="mp-skeleton" style={{ width: "2.25rem", height: "2.25rem", borderRadius: "var(--radius-sm)", flex: "none" }} />
                <div style={{ flex: 1 }}>
                  <div className="mp-skeleton" style={{ height: "0.9rem", width: "60%" }} />
                  <div className="mp-skeleton mt-2" style={{ height: "0.75rem", width: "90%" }} />
                  <div className="mp-skeleton mt-2" style={{ height: "0.7rem", width: "35%" }} />
                </div>
              </div>
            </div>
          ))}
        </div>
      ) : notifications.isError || !notifications.data ? (
        <ErrorState message="We could not load your notifications." onRetry={() => void notifications.refetch()} />
      ) : notifications.data.items.length === 0 ? (
        <EmptyState
          title={filtered ? "Nothing matches those filters" : "You're all caught up"}
          body={
            filtered
              ? "Try a different type, or clear the unread filter."
              : "New notifications about your orders and account activity will appear here."
          }
          action={
            filtered ? (
              <Link href="/notifications" className="btn btn-sm btn-primary">
                Clear filters
              </Link>
            ) : (
              <Link href="/products" className="btn btn-sm btn-primary">
                Continue shopping
              </Link>
            )
          }
        />
      ) : (
        <>
          <ul className="list-unstyled mb-0">
            {notifications.data.items.map(notification => {
              const Icon = notificationIcon(notification.type);
              const href = resolveNotificationHref(notification.link);

              return (
                <li
                  key={notification.id}
                  className={cx("mp-notification", !notification.isRead && "is-unread")}
                  style={{ borderBottom: "1px solid var(--border)", padding: "var(--space-3) 0" }}
                >
                  <div className="d-flex align-items-start" style={{ gap: "var(--space-3)" }}>
                    <span
                      aria-hidden
                      style={{
                        color: notification.isRead ? "var(--text-subtle)" : "var(--brand-600)",
                        marginTop: "0.15rem",
                        display: "grid",
                        placeItems: "center",
                        width: "2.25rem",
                        height: "2.25rem",
                        borderRadius: "var(--radius-sm)",
                        backgroundColor: "var(--bg-subtle)",
                        flex: "none",
                      }}
                    >
                      <Icon size={16} />
                    </span>

                    <div style={{ flex: 1, minWidth: 0 }}>
                      <p style={{ margin: 0, fontSize: "var(--fs-sm)", fontWeight: notification.isRead ? 400 : 600 }}>
                        {notification.title}
                      </p>
                      <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{notification.body}</p>
                      <p style={{ margin: "var(--space-1) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                        <time dateTime={notification.createdAt} title={formatDate(notification.createdAt)}>
                          {formatRelative(notification.createdAt)}
                        </time>
                        {" · "}
                        {notificationKind(notification.type)}
                      </p>
                      {href ? (
                        <Link
                          href={href}
                          className="btn btn-sm btn-outline-secondary mt-2"
                          onClick={() => {
                            if (!notification.isRead) {
                              markRead.mutate(notification.id);
                            }
                          }}
                        >
                          {href.startsWith("/orders") ? "View order" : "Open"}
                        </Link>
                      ) : null}
                    </div>

                    {!notification.isRead ? (
                      <button
                        type="button"
                        className="btn btn-sm"
                        onClick={() => markRead.mutate(notification.id)}
                        disabled={markRead.isPending}
                        aria-label={`Mark "${notification.title}" as read`}
                        style={{ color: "var(--text-subtle)", flex: "none" }}
                      >
                        <Check size={16} aria-hidden />
                      </button>
                    ) : null}

                    <button
                      type="button"
                      className="btn btn-sm"
                      onClick={() => remove.mutate(notification.id)}
                      disabled={remove.isPending}
                      aria-label={`Dismiss "${notification.title}"`}
                      style={{ color: "var(--text-subtle)", flex: "none" }}
                    >
                      <X size={16} aria-hidden />
                    </button>
                  </div>
                </li>
              );
            })}
          </ul>

          <Pagination
            page={notifications.data.page}
            totalPages={notifications.data.totalPages}
            query={{ unreadOnly: unreadOnly || undefined, type }}
            basePath="/notifications"
          />
        </>
      )}
    </div>
  );
}

export function NotificationsPage() {
  return (
    <RequireAuth returnTo="/notifications">
      <NotificationList />
    </RequireAuth>
  );
}
