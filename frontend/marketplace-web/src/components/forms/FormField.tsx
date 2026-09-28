/**
 * A labelled form field.
 *
 * Every input in the app goes through this so that the label, the error and the control are
 * wired together the same way: an error is announced, described by its input, and shown next to
 * it. A field whose message is only red text is invisible to a screen reader and easy to miss
 * for everyone else.
 */

"use client";

import { forwardRef, useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from "react";

interface FieldShellProps {
  label: string;
  error?: string;
  hint?: ReactNode;
  required?: boolean;
  children: (ids: { id: string; describedBy: string | undefined; invalid: boolean }) => ReactNode;
}

/** The visual frame: label, control, message. */
function FieldShell({ label, error, hint, required, children }: FieldShellProps) {
  const id = useId();
  const messageId = `${id}-message`;
  const hintId = `${id}-hint`;
  const describedBy = [error ? messageId : null, hint ? hintId : null].filter(Boolean).join(" ") || undefined;

  return (
    <div className="mp-stack-sm">
      <label htmlFor={id} className="mp-metric-label">
        {label}
        {required ? (
          <>
            {" "}
            <span aria-hidden style={{ color: "var(--danger)" }}>
              *
            </span>
            <span className="visually-hidden">(required)</span>
          </>
        ) : null}
      </label>

      {children({ id, describedBy, invalid: Boolean(error) })}

      {hint ? (
        <p id={hintId} style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
          {hint}
        </p>
      ) : null}

      {/*
        The message is in the document even when there is nothing to say, so a control that
        becomes invalid does not have its description appear and disappear under the reader.
      */}
      <p
        id={messageId}
        role={error ? "alert" : undefined}
        style={{ margin: 0, color: error ? "var(--danger)" : "transparent", fontSize: "var(--fs-xs)", minHeight: "1rem" }}
      >
        {error ?? "\u00a0"}
      </p>
    </div>
  );
}

function fieldStyle(invalid: boolean): React.CSSProperties {
  return {
    width: "100%",
    padding: "0.55rem 0.7rem",
    borderRadius: "var(--radius-sm)",
    border: `1px solid ${invalid ? "var(--danger)" : "var(--border)"}`,
    backgroundColor: "var(--bg-surface)",
    color: "var(--text)",
    fontSize: "var(--fs-sm)",
  };
}

interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "id"> {
  label: string;
  error?: string;
  hint?: ReactNode;
}

export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField(
  { label, error, hint, required, ...inputProps },
  ref,
) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      {({ id, describedBy, invalid }) => (
        <input
          {...inputProps}
          ref={ref}
          id={id}
          aria-describedby={describedBy}
          aria-invalid={invalid || undefined}
          style={{ ...fieldStyle(invalid), ...inputProps.style }}
        />
      )}
    </FieldShell>
  );
});

interface FieldWithButtonProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "id" | "children"> {
  label: string;
  error?: string;
  hint?: ReactNode;
  required?: boolean;
  /** The control inside the field, usually a button: show a password, clear a search. */
  children: (fields: { id: string; describedBy: string | undefined; invalid: boolean }) => ReactNode;
}

/**
 * A text field with a control inside it.
 *
 * Written rather than assembled at each call site, because the reason it exists is that the label,
 * the error announcement and the invalid border all have to be wired to the input, and doing that
 * by hand in a sign-in form is how a password field ends up with an error message no screen reader
 * ever announces.
 */
export function FieldWithButton({ label, error, hint, required, children, ...inputProps }: FieldWithButtonProps) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      {({ id, describedBy, invalid }) => (
        <div className="mp-field-affix">
          <input
            {...inputProps}
            id={id}
            aria-describedby={describedBy}
            aria-invalid={invalid || undefined}
            style={fieldStyle(invalid)}
          />
          {children({ id, describedBy, invalid })}
        </div>
      )}
    </FieldShell>
  );
}

interface TextAreaFieldProps extends Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, "id"> {
  label: string;
  error?: string;
  hint?: ReactNode;
}

export const TextAreaField = forwardRef<HTMLTextAreaElement, TextAreaFieldProps>(function TextAreaField(
  { label, error, hint, required, ...textareaProps },
  ref,
) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      {({ id, describedBy, invalid }) => (
        <textarea
          {...textareaProps}
          ref={ref}
          id={id}
          aria-describedby={describedBy}
          aria-invalid={invalid || undefined}
          style={{ ...fieldStyle(invalid), minHeight: "6rem", ...textareaProps.style }}
        />
      )}
    </FieldShell>
  );
});

interface SelectFieldProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, "id"> {
  label: string;
  error?: string;
  hint?: ReactNode;
  children: ReactNode;
}

export const SelectField = forwardRef<HTMLSelectElement, SelectFieldProps>(function SelectField(
  { label, error, hint, required, children, ...selectProps },
  ref,
) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      {({ id, describedBy, invalid }) => (
        <select
          {...selectProps}
          ref={ref}
          id={id}
          aria-describedby={describedBy}
          aria-invalid={invalid || undefined}
          style={{ ...fieldStyle(invalid), ...selectProps.style }}
        >
          {children}
        </select>
      )}
    </FieldShell>
  );
});

/** A checkbox with its label, for the choices a form asks about rather than collects. */
export function CheckField({
  label,
  error,
  hint,
  ...inputProps
}: Omit<InputHTMLAttributes<HTMLInputElement>, "id"> & { label: string; error?: string; hint?: ReactNode }) {
  const id = useId();
  const messageId = `${id}-message`;

  return (
    <div>
      <div className="d-flex align-items-start" style={{ gap: "0.5rem" }}>
        <input
          {...inputProps}
          id={id}
          type="checkbox"
          aria-describedby={error ? messageId : undefined}
          aria-invalid={error ? true : undefined}
          style={{ marginTop: "0.2rem" }}
        />
        <label htmlFor={id} style={{ fontSize: "var(--fs-sm)" }}>
          {label}
        </label>
      </div>

      {hint ? (
        <p style={{ margin: "0.25rem 0 0 1.5rem", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{hint}</p>
      ) : null}

      <p
        id={messageId}
        role={error ? "alert" : undefined}
        style={{ margin: "0.25rem 0 0 1.5rem", color: error ? "var(--danger)" : "transparent", fontSize: "var(--fs-xs)" }}
      >
        {error ?? "\u00a0"}
      </p>
    </div>
  );
}
