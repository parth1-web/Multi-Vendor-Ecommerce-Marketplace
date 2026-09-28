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
import { Eye, EyeOff } from "lucide-react";

import { AuthShell, DemoAccounts } from "@/components/auth/AuthShell";
import { FieldWithButton, TextField } from "@/components/forms/FormField";
import { authApi } from "@/features/auth/api/authApi";
import { loginSchema, type LoginValues } from "@/features/auth/schemas";
import { errorMessage, fieldErrors, isSignInFailure } from "@/lib/errors";
import { useAuth } from "@/providers/AuthProvider";
import type { AuthShellProps } from "@/components/auth/AuthShell";

type LoginStats = AuthShellProps["stats"];

export function LoginForm({ returnUrl, reason, stats }: { returnUrl: string; reason?: string | null; stats?: LoginStats }) {
  const router = useRouter();
  const { signIn } = useAuth();
  const [formError, setFormError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);

  const {
    register,
    handleSubmit,
    setError,
    setValue,
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
    <AuthShell
      stats={stats}
      title="Sign in"
      subtitle={reason ?? "Pick up where you left off, or start a basket you can come back to."}
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
          placeholder="you@example.com"
          error={errors.email?.message}
          {...register("email")}
        />

        {/*
          The password field, with a button inside it. Two people fail to sign in for opposite
          reasons — one cannot see what they typed, the other cannot see what they have just
          removed — and the same control answers both, which a checkbox in the corner does not.
        */}
        <FieldWithButton
          label="Password"
          type={showPassword ? "text" : "password"}
          autoComplete="current-password"
          required
          error={errors.password?.message}
          {...register("password")}
        >
          {({ id }) => (
            <button
              type="button"
              className="mp-field-affix-btn"
              onClick={() => setShowPassword(current => !current)}
              aria-label={showPassword ? "Hide the password" : "Show the password"}
              aria-pressed={showPassword}
              aria-controls={id}
              title={showPassword ? "Hide the password" : "Show the password"}
            >
              {showPassword ? <EyeOff size={16} aria-hidden /> : <Eye size={16} aria-hidden />}
            </button>
          )}
        </FieldWithButton>

        <div className="d-flex justify-content-between align-items-center flex-wrap" style={{ gap: "var(--space-2)" }}>
          <Link href="/forgot-password" style={{ fontSize: "var(--fs-sm)" }}>
            Forgotten your password?
          </Link>
        </div>

        <button type="submit" className="btn btn-primary w-100" disabled={isSubmitting}>
          {isSubmitting ? (
            <>
              <span className="spinner-border spinner-border-sm me-2" aria-hidden />
              Signing in…
            </>
          ) : (
            "Sign in"
          )}
        </button>
      </form>

      <DemoAccounts
        onPick={(email, password) => {
          setValue("email", email, { shouldValidate: true });
          setValue("password", password, { shouldValidate: true });
          setFormError(null);
        }}
      />

      <p style={{ margin: "var(--space-5) 0 0", textAlign: "center", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        No account yet? <Link href="/register">Create one</Link>
      </p>
    </AuthShell>
  );
}
