/**
 * Creating an account.
 *
 * The one decision on this page is whether you are here to buy or to sell, and it is a
 * checkbox rather than a hidden field: a seller account is a different product with a different
 * review process, and finding that out after filling in a form is how people give up.
 */

"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import { CheckField, TextField } from "@/components/forms/FormField";
import { AnonymousOnly } from "@/features/auth/components/AnonymousOnly";
import { AuthPanel } from "@/features/auth/components/AuthPanel";
import { authApi } from "@/features/auth/api/authApi";
import { registerSchema, type RegisterValues } from "@/features/auth/schemas";
import { errorMessage, fieldErrors } from "@/lib/errors";
import { useAuth } from "@/providers/AuthProvider";

export default function RegisterPage() {
  return (
    <AnonymousOnly>
      <RegisterForm />
    </AnonymousOnly>
  );
}

function RegisterForm() {
  const router = useRouter();
  const { signIn } = useAuth();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    watch,
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

  const wantsToSell = watch("wantsToSell");

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
    <AuthPanel
      title="Create an account"
      subtitle="One account for buying, selling, orders and everything in between."
      footer={
        <>
          Already have one? <Link href="/login">Sign in</Link>
        </>
      }
    >
      <form onSubmit={handleSubmit(onSubmit)} noValidate className="mp-stack">
        {formError ? (
          <p role="alert" className="mp-alert mp-alert-danger">
            {formError}
          </p>
        ) : null}

        <div className="row g-2">
          <div className="col-12 col-sm-6">
            <TextField label="First name" autoComplete="given-name" required error={errors.firstName?.message} {...register("firstName")} />
          </div>
          <div className="col-12 col-sm-6">
            <TextField label="Last name" autoComplete="family-name" required error={errors.lastName?.message} {...register("lastName")} />
          </div>
        </div>

        <TextField
          label="Email"
          type="email"
          inputMode="email"
          autoComplete="email"
          required
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

        <TextField
          label="Password"
          type="password"
          autoComplete="new-password"
          required
          hint="At least 8 characters, with an upper-case letter, a lower-case letter and a digit."
          error={errors.password?.message}
          {...register("password")}
        />

        <TextField
          label="Confirm password"
          type="password"
          autoComplete="new-password"
          required
          error={errors.confirmPassword?.message}
          {...register("confirmPassword")}
        />

        <CheckField
          label="I want to sell on this marketplace"
          hint="Your account is reviewed before you can list anything."
          error={errors.wantsToSell?.message}
          {...register("wantsToSell")}
        />

        <CheckField
          label="I accept the terms of sale and the privacy policy"
          error={errors.terms?.message}
          {...register("terms")}
        />

        <button type="submit" className="btn btn-primary w-100" disabled={isSubmitting}>
          {isSubmitting ? "Creating your account…" : wantsToSell ? "Create a seller account" : "Create an account"}
        </button>
      </form>
    </AuthPanel>
  );
}
