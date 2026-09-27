/**
 * Accounts on the marketplace.
 *
 * A role change and a deactivation both take effect immediately, so each one says what it will
 * do and asks to be confirmed by naming the account. An admin console that can lock people out
 * by one mis-click is a console nobody trusts.
 */

"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { cx, formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { UserRole } from "@/types/auth";

const ROLES: (UserRole | "")[] = ["", "Customer", "Seller", "Admin"];

export function AdminUsers() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [role, setRole] = useState<UserRole | "">("");
  const [confirming, setConfirming] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const users = useQuery({
    queryKey: queryKeys.admin.users({ page, search, role }),
    queryFn: () => adminApi.users({ page, search: search || undefined, role: role || undefined }),
  });

  const changeRole = useMutation({
    mutationFn: ({ id, next }: { id: string; next: UserRole }) => adminApi.setUserRole(id, next),
    onSuccess: async () => {
      setConfirming(null);
      setActionError(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.admin.all });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  const setActive = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => adminApi.setUserActive(id, isActive),
    onSuccess: async () => {
      setConfirming(null);
      setActionError(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.admin.all });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  return (
    <div className="mp-stack">
      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      <div className="row g-2">
        <div className="col-12 col-md-7">
          <input
            className="mp-input"
            placeholder="Search by name or email"
            aria-label="Search accounts"
            value={search}
            onChange={event => {
              setSearch(event.target.value);
              setPage(1);
            }}
          />
        </div>
        <div className="col-12 col-md-5">
          <select
            className="mp-input"
            aria-label="Filter by role"
            value={role}
            onChange={event => {
              setRole(event.target.value as UserRole | "");
              setPage(1);
            }}
          >
            {ROLES.map(value => (
              <option key={value || "all"} value={value}>
                {value || "Every role"}
              </option>
            ))}
          </select>
        </div>
      </div>

      {users.isPending ? (
        <div className="mp-skeleton" style={{ height: "14rem", borderRadius: "var(--radius)" }} />
      ) : users.isError || !users.data ? (
        <ErrorState message="We could not load the accounts." />
      ) : users.data.items.length === 0 ? (
        <EmptyState title="No accounts match" body="Try a different search or role." />
      ) : (
        <div className="mp-table-wrap">
          <table className="mp-table">
            <caption className="visually-hidden">Accounts</caption>
            <thead>
              <tr>
                <th scope="col">Person</th>
                <th scope="col">Role</th>
                <th scope="col">Joined</th>
                <th scope="col">Last seen</th>
                <th scope="col">State</th>
                <th scope="col" />
              </tr>
            </thead>
            <tbody>
              {users.data.items.map(user => (
                <tr key={user.id}>
                  <td>
                    <strong>{user.fullName}</strong>
                    <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{user.email}</span>
                  </td>
                  <td>
                    <StatusBadge tone={user.role === "Customer" ? "info" : "violet"}>{user.role}</StatusBadge>
                  </td>
                  <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{formatDate(user.createdAt)}</td>
                  <td style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                    {user.lastLoginAt ? formatDate(user.lastLoginAt) : "never"}
                  </td>
                  <td>
                    {user.isActive ? (
                      <StatusBadge tone="success">Active</StatusBadge>
                    ) : (
                      <StatusBadge tone="danger">Deactivated</StatusBadge>
                    )}
                    {user.isEmailConfirmed ? null : (
                      <span style={{ display: "block", color: "var(--warning)", fontSize: "var(--fs-xs)" }}>email unconfirmed</span>
                    )}
                  </td>
                  <td>
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-secondary"
                      onClick={() => {
                        setConfirming(confirming === user.id ? null : user.id);
                        setActionError(null);
                      }}
                      aria-expanded={confirming === user.id}
                    >
                      {confirming === user.id ? "Close" : "Manage"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {confirming ? (
        <ManagePanel
          user={users.data?.items.find(item => item.id === confirming)}
          busy={changeRole.isPending || setActive.isPending}
          onRole={next => changeRole.mutate({ id: confirming, next })}
          onActive={isActive => setActive.mutate({ id: confirming, isActive })}
        />
      ) : null}

      {users.data && users.data.totalPages > 1 ? (
        <nav aria-label="Account pages" className="d-flex justify-content-between align-items-center">
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>
            Previous
          </button>
          <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            Page {page} of {users.data.totalPages}
          </span>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setPage(p => Math.min(users.data!.totalPages, p + 1))}
            disabled={page >= users.data.totalPages}
          >
            Next
          </button>
        </nav>
      ) : null}
    </div>
  );
}

function ManagePanel({
  user,
  busy,
  onRole,
  onActive,
}: {
  user?: { id: string; fullName: string; email: string; role: UserRole; isActive: boolean };
  busy: boolean;
  onRole: (role: UserRole) => void;
  onActive: (isActive: boolean) => void;
}) {
  if (!user) {
    return null;
  }

  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-label={`Manage ${user.email}`}>
      <h2 style={{ margin: 0, fontSize: "var(--fs-h3)" }}>{user.fullName}</h2>
      <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{user.email}</p>

      <div className="mp-stack-sm mt-3">
        <div>
          <label htmlFor={`role-${user.id}`} className="mp-metric-label">
            Role
          </label>
          <select
            id={`role-${user.id}`}
            className="mp-input"
            value={user.role}
            onChange={event => onRole(event.target.value as UserRole)}
            disabled={busy}
          >
            <option value="Customer">Customer</option>
            <option value="Seller">Seller</option>
            <option value="Admin">Admin</option>
            <option value="SuperAdmin">Super admin</option>
          </select>
          <p style={{ margin: "0.25rem 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
            Changing a role takes effect the next time they load a page.
          </p>
        </div>

        <div>
          <button
            type="button"
            className={cx("btn btn-sm", user.isActive ? "" : "btn-primary")}
            onClick={() => onActive(!user.isActive)}
            disabled={busy}
            style={user.isActive ? { color: "var(--danger)" } : undefined}
          >
            {user.isActive ? `Deactivate ${user.email}` : `Reactivate ${user.email}`}
          </button>
        </div>
      </div>
    </section>
  );
}
