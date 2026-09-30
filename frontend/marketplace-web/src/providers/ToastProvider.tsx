/**
 * Toasts. A queue in a provider rather than a library, because the only requirement is four
 * tones and an auto-dismiss, and a toast that pulls in its own context is a second source of
 * truth about what the user has been told.
 */

"use client";

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";

import { cx } from "@/lib/format";

export type ToastTone = "success" | "danger" | "warning" | "info";

interface Toast {
  id: number;
  tone: ToastTone;
  title: string;
  body?: string;
  /**
   * One follow-up action, for the rare toast that can genuinely be undone. The viewport renders
   * it as a real button rather than text, and running it dismisses the toast first so the
   * follow-up never stacks on top of the thing it undoes.
   */
  action?: { label: string; onClick: () => void };
}

interface ToastContextValue {
  toasts: Toast[];
  push: (toast: Omit<Toast, "id">) => void;
  dismiss: (id: number) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

let nextId = 1;

const AUTO_DISMISS_MS = 5_000;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const push = useCallback(
    (toast: Omit<Toast, "id">) => {
      const id = nextId++;
      setToasts((current) => [...current, { ...toast, id }]);

      setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
    },
    [dismiss],
  );

  const value = useMemo(() => ({ toasts, push, dismiss }), [toasts, push, dismiss]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <ToastViewport toasts={toasts} onDismiss={dismiss} />
    </ToastContext.Provider>
  );
}

function ToastViewport({ toasts, onDismiss }: { toasts: Toast[]; onDismiss: (id: number) => void }) {
  return (
    <div
      className="mp-toast-viewport"
      role="status"
      aria-live="polite"
      style={{
        position: "fixed",
        insetInlineEnd: "var(--space-4)",
        insetBlockEnd: "var(--space-4)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--space-2)",
        zIndex: 1080,
        maxWidth: "22rem",
      }}
    >
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className={cx("mp-card", "mp-toast", `mp-badge-${toast.tone}`)}
          onClick={() => onDismiss(toast.id)}
          style={{ padding: "var(--space-3) var(--space-4)", cursor: "pointer", borderLeftWidth: 4 }}
        >
          <div style={{ fontWeight: 600, color: "var(--text)" }}>{toast.title}</div>
          {toast.body ? (
            <div style={{ fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>{toast.body}</div>
          ) : null}
          {toast.action ? (
            <div style={{ marginTop: "var(--space-2)" }}>
              <button
                type="button"
                className="btn btn-sm btn-outline-secondary"
                onClick={(event) => {
                  event.stopPropagation();
                  onDismiss(toast.id);
                  toast.action?.onClick();
                }}
              >
                {toast.action.label}
              </button>
            </div>
          ) : null}
        </div>
      ))}
    </div>
  );
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);

  if (!context) {
    throw new Error("useToast must be used inside ToastProvider");
  }

  return context;
}
