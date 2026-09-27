/**
 * Signing in.
 *
 * The page that renders this is a server component so that the form is in the HTML rather than
 * appearing only once JavaScript has run: the whole purpose of the page is a form, and a form
 * that is not there until hydration is a form that does not work for anyone waiting for it.
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
import { loginSchema, type LoginValues } from "@/features/auth/schemas";
import { errorMessage, fieldErrors, isSignInFailure } from "@/lib/errors";
import { useAuth } from "@/providers/AuthProvider";

export function LoginForm({ returnUrl, reason }: { returnUrl: string; reason?: string | null }) {
  const router = useRouter();
  const { signIn } = useAuth();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  });

  async function onSubmit(values: LoginValues) {
    setFormError(null);

    try {
      const session = await authApi.login(values);
      signIn(session.accessToken, session.user);
      router.replace(returnUrl);
    } catch (error) {
      // A wrong email and a wrong password give the same answer, so the message is attached to
      // the form rather than to whichever box the person happened to use.
      if (isSignInFailure(error)) {
        setFormError(errorMessage(error, "That email and password do not match an account."));
        return;
      }

      for (const [field, message] of Object.entries(fieldErrors(error))) {
        if (field === "email" || field === "password") {
          setError(field, { message });
        }
      }

      setFormError(errorMessage(error));
    }
  }

  return (
    <AuthPanel
      title="Sign in"
      subtitle={reason ?? "Pick up where you left off, or start a basket you can come back to."}
      footer={
        <>
          No account yet? <Link href="/register">Create one</Link>
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
          autoComplete="email"
          inputMode="email"
          required
          error={errors.email?.message}
          {...register("email")}
        />

        <TextField
          label="Password"
          type="password"
          autoComplete="current-password"
          required
          error={errors.password?.message}
          {...register("password")}
        />

        <div className="d-flex justify-content-between align-items-center">
          <Link href="/forgot-password" style={{ fontSize: "var(--fs-sm)" }}>
            Forgotten your password?
          </Link>
        </div>

        <button type="submit" className="btn btn-primary w-100" disabled={isSubmitting}>
          {isSubmitting ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </AuthPanel>
  );
}
