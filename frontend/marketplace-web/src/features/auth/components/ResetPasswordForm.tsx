/**
 * Choosing a new password from a reset link.
 *
 * A server page, so the form is in the HTML, and so the token is read once here and handed to
 * the form rather than being kept in client state: a token left lying in the client is a token
 * that can be replayed from anywhere it leaked to. It is spent once and then taken out of the
 * address bar, because a link that has been used should not still be sitting in history.
 */

"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import { TextField } from "@/components/forms/FormField";
import { AuthPanel } from "@/features/auth/components/AuthPanel";
import { authApi } from "@/features/auth/api/authApi";
import { resetPasswordSchema, type ResetPasswordValues } from "@/features/auth/schemas";
import { errorMessage } from "@/lib/errors";

export function ResetPasswordForm({ token }: { token: string | null }) {
  const router = useRouter();
  const [formError, setFormError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ResetPasswordValues>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: { password: "", confirmPassword: "" },
  });

  async function onSubmit(values: ResetPasswordValues) {
    setFormError(null);

    if (!token) {
      setFormError("This link is missing its token. Request a new one and use the newest link.");
      return;
    }

    try {
      await authApi.resetPassword({ token, password: values.password, confirmPassword: values.confirmPassword });
      setDone(true);
      router.replace("/reset-password");
    } catch (error) {
      setFormError(errorMessage(error, "This reset link is no longer valid. Request a new one."));
    }
  }

  if (done) {
    return (
      <AuthPanel
        title="Password changed"
        subtitle="You can sign in with it now."
        footer={
          <>
            Changed your mind? <Link href="/login">Sign in</Link>
          </>
        }
      >
        <p className="mp-alert mp-alert-success" role="status">
          Your password has been changed and every other session on this account has been signed out.
        </p>
      </AuthPanel>
    );
  }

  return (
    <AuthPanel
      title="Choose a new password"
      subtitle="This link works once and expires two hours after it was sent."
      footer={
        <>
          Need a new link? <Link href="/forgot-password">Request one</Link>
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
          label="New password"
          type="password"
          autoComplete="new-password"
          required
          hint="At least 8 characters, with an upper-case letter, a lower-case letter and a digit."
          error={errors.password?.message}
          {...register("password")}
        />

        <TextField
          label="Confirm new password"
          type="password"
          autoComplete="new-password"
          required
          error={errors.confirmPassword?.message}
          {...register("confirmPassword")}
        />

        <button type="submit" className="btn btn-primary w-100" disabled={isSubmitting}>
          {isSubmitting ? "Saving…" : "Save my new password"}
        </button>
      </form>
    </AuthPanel>
  );
}
