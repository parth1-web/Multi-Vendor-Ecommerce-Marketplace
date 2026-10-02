"use client";

/**
 * A confirmation for something that cannot be undone.
 *
 * One component rather than a modal written per screen, because the three things that make a
 * destructive dialog usable are the three most often left out: the dialog is labelled, the focus
 * lands inside it, and the confirming button says in words what it will do ("Delete this listing")
 * instead of "OK". A destructive action that is only signalled by the colour of a button is a
 * destructive action some people will trigger.
 *
 * While `busy` is true the dialog cannot be dismissed by clicking away or pressing Escape, because
 * the request is already in flight and a closed dialog reads as a cancelled one.
 */

import { useEffect, useId, useRef, type ReactNode } from "react";
import { Modal } from "react-bootstrap";

interface ConfirmDialogProps {
  show: boolean;
  title: string;
  children: ReactNode;
  confirmLabel: string;
  cancelLabel?: string;
  tone?: "danger" | "primary";
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  show,
  title,
  children,
  confirmLabel,
  cancelLabel = "Keep it",
  tone = "danger",
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const confirmRef = useRef<HTMLButtonElement>(null);
  // Per-instance rather than a fixed id, so two dialogs on one page cannot collide and leave the
  // first one unlabelled.
  const instanceId = useId();
  const titleId = `${instanceId}-confirm-title`;

  // The action the dialog is about goes first, so a keyboard user is on the button rather than
  // hunting for it behind the body text.
  useEffect(() => {
    if (show) {
      confirmRef.current?.focus();
    }
  }, [show]);

  return (
    <Modal
      show={show}
      onHide={busy ? undefined : onCancel}
      centered
      backdrop={busy ? "static" : true}
      keyboard={!busy}
      aria-labelledby={titleId}
    >
      <Modal.Header closeButton={!busy}>
        <Modal.Title id={titleId} style={{ fontSize: "var(--fs-h3)" }}>
          {title}
        </Modal.Title>
      </Modal.Header>

      <Modal.Body style={{ fontSize: "var(--fs-sm)" }}>{children}</Modal.Body>

      <Modal.Footer>
        <button ref={confirmRef} type="button" className={`btn btn-sm btn-${tone}`} onClick={onConfirm} disabled={busy}>
          {busy ? "Working…" : confirmLabel}
        </button>
        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onCancel} disabled={busy}>
          {cancelLabel}
        </button>
      </Modal.Footer>
    </Modal>
  );
}