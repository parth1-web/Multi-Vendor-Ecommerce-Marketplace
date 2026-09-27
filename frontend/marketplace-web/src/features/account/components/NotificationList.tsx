/**
 * The notification list.
 *
 * Reading one is an action, not a page load: somebody triaging their list should not have to
 * wait for a round trip per row, so the row marks itself read and rolls back if the API
 * disagrees.
 */

"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BellRing, Check, X } from "lucide-react";

import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { RequireAuth } from "@/features/account/components/RequireAuth";
import { notificationApi } from "@/features/account/api/accountApi";
import { cx, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { NotificationPage, NotificationType } from "@/types/account";

function NotificationList() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [unreadOnly, setUnreadOnly] = useState(false);

  const notifications = useQuery({
    queryKey: queryKeys.notifications.list({ page, unreadOnly }),
    queryFn: () => notificationApi.list(page, 20, unreadOnly),
  });

  const markRead = useMutation({
    mutationFn: (id: string) => notificationApi.markRead(id),
    onMutate: async id => {
      // Optimistic: the row is read the moment it is clicked, and the list is refetched
      // afterwards so the count agrees with the server either way.
      await queryClient.cancelQueries({ queryKey: queryKeys.notifications.all });
      const previous = queryClient.getQueryData(queryKeys.notifications.list({ page, unreadOnly }));

      queryClient.setQueryData(queryKeys.notifications.list({ page, unreadOnly }), (current: NotificationPage | undefined) =>
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
        queryClient.setQueryData(queryKeys.notifications.list({ page, unreadOnly }), context.previous);
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

  if (notifications.isPending) {
    return (
      <div className="mp-stack-sm">
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="mp-skeleton" style={{ height: "4rem", borderRadius: "var(--radius)" }} />
        ))}
      </div>
    );
  }

  if (notifications.isError || !notifications.data) {
    return <ErrorState message="We could not load your notifications." />;
  }

  return (
    <div className="mp-stack">
      <div className="mp-spread">
        <label className="d-flex align-items-center" style={{ gap: "0.4rem", fontSize: "var(--fs-sm)" }}>
          <input type="checkbox" checked={unreadOnly} onChange={event => {
            setUnreadOnly(event.target.checked);
            setPage(1);
          }} />
          Unread only
        </label>

        <button
          type="button"
          className="btn btn-sm btn-outline-secondary"
          onClick={() => markAllRead.mutate()}
          disabled={markAllRead.isPending}
        >
          <Check size={14} aria-hidden className="me-1" />
          Mark everything read
        </button>
      </div>

      {notifications.data.items.length === 0 ? (
        <EmptyState title="Nothing to read" body="Order updates, refunds and seller news arrive here." />
      ) : (
        <ul className="list-unstyled mb-0">
          {notifications.data.items.map(notification => (
            <li
              key={notification.id}
              className={cx("mp-notification", !notification.isRead && "is-unread")}
              style={{ borderBottom: "1px solid var(--border)", padding: "var(--space-3) 0" }}
            >
              <div className="d-flex align-items-start" style={{ gap: "var(--space-3)" }}>
                <span aria-hidden style={{ color: notification.isRead ? "var(--text-subtle)" : "var(--brand-600)", marginTop: "0.15rem" }}>
                  <BellRing size={16} />
                </span>

                <div style={{ flex: 1, minWidth: 0 }}>
                  <p style={{ margin: 0, fontSize: "var(--fs-sm)", fontWeight: notification.isRead ? 400 : 600 }}>{notification.title}</p>
                  <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{notification.body}</p>
                  <p style={{ margin: "var(--space-1) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                    {describe(notification.type)} · {formatDate(notification.createdAt)}
                  </p>
                  {notification.link ? (
                    <Link href={notification.link} className="btn btn-sm btn-outline-secondary mt-2">
                      Go there
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
                    style={{ color: "var(--text-subtle)" }}
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
                  style={{ color: "var(--text-subtle)" }}
                >
                  <X size={16} aria-hidden />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {notifications.data.totalPages > 1 ? (
        <nav aria-label="Notification pages" className="d-flex justify-content-between align-items-center mt-3">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Older
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {notifications.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(notifications.data!.totalPages, p + 1))}
            disabled={page >= notifications.data.totalPages}
          >
            Newer
          </button>
        </nav>
      ) : null}
    </div>
  );
}

/** Spells the type out rather than printing the enum name, which means nothing to a shopper. */
function describe(type: NotificationType): string {
  switch (type) {
    case "OrderCreated":
    case "OrderConfirmed":
    case "OrderShipped":
    case "OrderDelivered":
    case "OrderCancelled":
      return "Order";
    case "PaymentSuccessful":
    case "PaymentFailed":
      return "Payment";
    case "RefundRequested":
    case "RefundApproved":
    case "RefundRejected":
      return "Refund";
    case "ProductApproved":
    case "ProductRejected":
      return "Your product";
    default:
      return "Update";
  }
}

export function NotificationsPage() {
  return (
    <RequireAuth returnTo="/notifications">
      <NotificationList />
    </RequireAuth>
  );
}
