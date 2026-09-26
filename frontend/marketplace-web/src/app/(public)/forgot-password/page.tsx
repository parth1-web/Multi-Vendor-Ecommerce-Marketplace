/**
 * Asking for a reset link.
 *
 * The page says the same thing whatever the address turns out to be, and it says it before the
 * request is sent. A form that answers "we sent you a link" for one address and "no such
 * account" for another is an account list, and people do use them as one.
 */

"use client";

import Link from "next/link";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import { TextField } from "@/components/forms/FormField";
import { AnonymousOnly } from "@/features/auth/components/AnonymousOnly";
import { AuthPanel } from "@/features/auth/components/AuthPanel";
import { authApi } from "@/features/auth/api/authApi";
import { forgotPasswordSchema, type ForgotPasswordValues } from "@/features/auth/schemas";
import { errorMessage } from "@/lib/errors";

export default function ForgotPasswordPage() {
  return (
    <AnonymousOnly>
      <ForgotPasswordForm />
    </AnonymousOnly>
  );
}

function ForgotPasswordForm() {
  const [sent, setSent] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [requested, setRequested] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ForgotPasswordValues>({
    resolver: zodResolver(forgotPasswordSchema),
    defaultValues: { email: "" },
  });

  async function onSubmit(values: ForgotPasswordValues) {
    setFormError(null);

    try {
      await authApi.forgotPassword(values.email);
      setRequested(values.email);
      setSent(true);
    } catch (error) {
      // A transport failure is worth saying: the link was not sent, and pretending otherwise
      // would leave someone waiting for an email that is not coming.
      setFormError(errorMessage(error, "We could not reach the server. Please try again in a moment."));
    }
  }

  if (sent) {
    return (
      <AuthPanel
        title="Check your email"
        subtitle="If that address has an account, a reset link is on its way."
        footer={
          <>
            Changed your mind? <Link href="/login">Back to sign in</Link>
          </>
        }
      >
        <p className="mp-alert mp-alert-success" role="status">
          We sent a reset link to <strong>{requested}</strong>. It works once and expires in two hours.
        </p>

        <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          Nothing arrived? Check the spam folder, or wait a few minutes before asking for another — sending a second
          link retires the first.
        </p>
      </AuthPanel>
    );
  }

  return (
    <AuthPanel
      title="Reset your password"
      subtitle="Tell us the address on the account and we will send a link to choose a new password."
      footer={
        <>
          Remembered it? <Link href="/login">Back to sign in</Link>
        </>
      }
    >
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="mp-stack">
        {formError ? (
          <p role="alert" className="mp-alert mp-alert-danger">
            {formError}
          </p>
        ) : null}

        <TextField
          label="Email"
          type="email"
          inputMode="email"
          autoComplete="email"
          required
          error={errors.email?.message}
          {...register("email")}
        />

        <button type="submit" className="btn btn-primary w-100" disabled={isSubmitting}>
          {isSubmitting ? "Sending…" : "Send me a reset link"}
        </button>
      </form>
    </AuthPanel>
  );
}
