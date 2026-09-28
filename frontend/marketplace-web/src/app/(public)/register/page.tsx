/**
 * Creating an account.
 *
 * The one decision on this page is whether you are here to buy or to sell, and it is a checkbox
 * rather than a hidden field: a seller account is a different product with a different review
 * process, and finding that out after filling in a form is how people give up. Choosing it also
 * changes what the button says and where you land afterwards, so the consequence is visible
 * before the click and not after it.
 */

"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Check, Eye, EyeOff, Store, UserRound } from "lucide-react";

import { AuthShell } from "@/components/auth/AuthShell";
import { CheckField, FieldWithButton, TextField } from "@/components/forms/FormField";
import { AnonymousOnly } from "@/features/auth/components/AnonymousOnly";
import { authApi } from "@/features/auth/api/authApi";
import { registerSchema, type RegisterValues } from "@/features/auth/schemas";
import { errorMessage, fieldErrors } from "@/lib/errors";
import { cx } from "@/lib/format";
import { useAuth } from "@/providers/AuthProvider";
import type { AuthShellProps } from "@/components/auth/AuthShell";

export default function RegisterPage({ stats }: { stats?: AuthShellProps["stats"] }) {
  return (
    <AnonymousOnly>
      <RegisterForm stats={stats} />
    </AnonymousOnly>
  );
}

function RegisterForm({ stats }: { stats?: AuthShellProps["stats"] }) {
  const router = useRouter();
  const { signIn } = useAuth();
  const [formError, setFormError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const [wantsToSell, setWantsToSell] = useState(false);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      firstName: "",
      lastName: "",
      email: "",
      phoneNumber: "",
      password: "",
      confirmPassword: "",
      wantsToSell: false,
      terms: false,
    },
  });

  async function onSubmit(values: RegisterValues) {
    setFormError(null);

    try {
      const session = await authApi.register({
        email: values.email,
        password: values.password,
        confirmPassword: values.confirmPassword,
        firstName: values.firstName,
        lastName: values.lastName,
        phoneNumber: values.phoneNumber?.trim() ? values.phoneNumber.trim() : null,
        role: values.wantsToSell ? "Seller" : "Customer",
      });

      signIn(session.accessToken, session.user);
      router.replace(values.wantsToSell ? "/seller/onboarding" : "/");
    } catch (error) {
      for (const [field, message] of Object.entries(fieldErrors(error))) {
        // The API names fields in the shape's own casing; the form registers them in camelCase.
        const key = field === "confirmpassword" ? "confirmPassword" : field;
        setError(key as keyof RegisterValues, { message });
      }

      setFormError(errorMessage(error));
    }
  }

  return (
    <AuthShell
      stats={stats}
      title="Create an account"
      subtitle="One account for buying, selling, orders and everything in between."
    >
      {/*
        The choice, as two cards rather than one checkbox: it is the largest decision on the page
        and it changes everything after it, so it is worth looking at rather than ticking.
      */}
      <fieldset style={{ border: 0, padding: 0, margin: "0 0 var(--space-4)" }}>
        <legend className="mp-metric-label p-0 mb-2">I want to</legend>
        <div className="row g-2">
          <div className="col-12 col-sm-6">
            <button
              type="button"
              className={cx("mp-choice", !wantsToSell && "is-selected")}
              aria-pressed={!wantsToSell}
              onClick={() => {
                setWantsToSell(false);
                setFormError(null);
              }}
            >
              <UserRound size={18} aria-hidden />
              <span>
                <strong>Buy things</strong>
                <small>Baskets, orders and saved items</small>
              </span>
              {!wantsToSell ? <Check size={16} aria-hidden className="ms-auto" /> : null}
            </button>
          </div>
          <div className="col-12 col-sm-6">
            <button
              type="button"
              className={cx("mp-choice", wantsToSell && "is-selected")}
              aria-pressed={wantsToSell}
              onClick={() => {
                setWantsToSell(true);
                setFormError(null);
              }}
            >
              <Store size={18} aria-hidden />
              <span>
                <strong>Sell things</strong>
                <small>Reviewed before you can list</small>
              </span>
              {wantsToSell ? <Check size={16} aria-hidden className="ms-auto" /> : null}
            </button>
          </div>
        </div>
        {/* The choice is held in state and sent in the body, but it is still a real form field:
            the browser can autofill it, and a password manager can fill the rest around it. */}
        <input type="hidden" {...register("wantsToSell")} value={wantsToSell ? "true" : "false"} />
      </fieldset>

      <form onSubmit={handleSubmit(onSubmit)} noValidate className="mp-stack">
        {formError ? (
          <p role="alert" className="mp-alert mp-alert-danger">
            {formError}
          </p>
        ) : null}

        <div className="row g-2">
          <div className="col-12 col-sm-6">
            <TextField
              label="First name"
              autoComplete="given-name"
              required
              error={errors.firstName?.message}
              {...register("firstName")}
            />
          </div>
          <div className="col-12 col-sm-6">
            <TextField
              label="Last name"
              autoComplete="family-name"
              required
              error={errors.lastName?.message}
              {...register("lastName")}
            />
          </div>
        </div>

        <TextField
          label="Email"
          type="email"
          inputMode="email"
          autoComplete="email"
          required
          placeholder="you@example.com"
          error={errors.email?.message}
          {...register("email")}
        />

        <TextField
          label="Phone"
          type="tel"
          inputMode="tel"
          autoComplete="tel"
          hint="Optional. Sellers are asked for one before they can be paid out."
          error={errors.phoneNumber?.message}
          {...register("phoneNumber")}
        />

        <FieldWithButton
          label="Password"
          type={showPassword ? "text" : "password"}
          autoComplete="new-password"
          required
          hint="At least 8 characters, with an upper-case letter, a lower-case letter and a digit."
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

        <TextField
          label="Confirm password"
          type={showPassword ? "text" : "password"}
          autoComplete="new-password"
          required
          error={errors.confirmPassword?.message}
          {...register("confirmPassword")}
        />

        <CheckField
          label="I accept the terms of sale and the privacy policy"
          error={errors.terms?.message}
          {...register("terms")}
        />

        <button type="submit" className="btn btn-primary w-100" disabled={isSubmitting}>
          {isSubmitting ? (
            <>
              <span className="spinner-border spinner-border-sm me-2" aria-hidden />
              Creating your account…
            </>
          ) : wantsToSell ? (
            "Create a seller account"
          ) : (
            "Create an account"
          )}
        </button>
      </form>

      <p style={{ margin: "var(--space-5) 0 0", textAlign: "center", color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
        Already have one? <Link href="/login">Sign in</Link>
      </p>
    </AuthShell>
  );
}
