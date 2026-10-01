/**
 * The account page: who you are, and how to change it.
 *
 * Also where the password is changed, which is the one action here that ends every session
 * including this one — so it says so before it does it rather than after.
 */

"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { z } from "zod";

import { TextField } from "@/components/forms/FormField";
import { StatusBadge } from "@/components/shared/Feedback";
import { RequireAuth } from "@/features/account/components/RequireAuth";
import { authApi } from "@/features/auth/api/authApi";
import { passwordRules } from "@/features/auth/schemas";
import { errorMessage, fieldErrors } from "@/lib/errors";
import { formatDate } from "@/lib/format";
import { useAuth } from "@/providers/AuthProvider";

const profileSchema = z.object({
  firstName: z.string().trim().min(1, "Enter your first name.").max(100),
  lastName: z.string().trim().min(1, "Enter your last name.").max(100),
  phoneNumber: z
    .string()
    .trim()
    .regex(/^[+0-9 ()-]{6,20}$/, "Enter a valid phone number.")
    .optional()
    .or(z.literal("")),
});

const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, "Enter your current password."),
    newPassword: passwordRules,
    confirmPassword: z.string().min(1, "Confirm your new password."),
  })
  .refine(values => values.newPassword === values.confirmPassword, {
    message: "The passwords do not match.",
    path: ["confirmPassword"],
  })
  .refine(values => values.newPassword !== values.currentPassword, {
    message: "The new password must be different from the current one.",
    path: ["newPassword"],
  });

type ProfileValues = z.infer<typeof profileSchema>;
type PasswordValues = z.infer<typeof passwordSchema>;

function AccountPanel() {
  const { user, signOut } = useAuth();
  const queryClient = useQueryClient();
  const router = useRouter();

  const [profileError, setProfileError] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);


  const profile = useForm<ProfileValues>({
    resolver: zodResolver(profileSchema),
    defaultValues: {
      firstName: user?.firstName ?? "",
      lastName: user?.lastName ?? "",
      phoneNumber: user?.phoneNumber ?? "",
    },
  });

  const password = useForm<PasswordValues>({
    resolver: zodResolver(passwordSchema),
    defaultValues: { currentPassword: "", newPassword: "", confirmPassword: "" },
  });

  const saveProfile = useMutation({
    mutationFn: (values: ProfileValues) => authApi.updateProfile(values),
    onSuccess: async () => {
      // The name and role are read from the session, so the cached identity has to be re-read
      // rather than patched: the header would otherwise keep showing the old name.
      await queryClient.invalidateQueries({ queryKey: ["auth", "me"] });
      setProfileError(null);
    },
  });

  const changePassword = useMutation({
    mutationFn: (values: PasswordValues) => authApi.changePassword(values),
    onSuccess: () => {
      setPasswordError(null);
      password.reset();
    },
  });


  async function onProfileSubmit(values: ProfileValues) {
    setProfileError(null);

    try {
      await saveProfile.mutateAsync(values);
    } catch (error) {
      for (const [field, message] of Object.entries(fieldErrors(error))) {
        const key = field.toLowerCase();
        if (key in values) {
          profile.setError(key as keyof ProfileValues, { message });
        }
      }

      setProfileError(errorMessage(error));
    }
  }

  async function onPasswordSubmit(values: PasswordValues) {
    setPasswordError(null);

    try {

      await changePassword.mutateAsync(values);

      // The server has revoked every session, including this one's, so the access token still
      // in memory is the only thing left of it. Ending it here is what makes the sentence under
      // the heading true: a password change that leaves you signed in on the device you changed
      // it from has not really signed you out anywhere you were not already looking.
      await signOut();
      router.replace("/login?reason=password-changed");
    } catch (error) {
      for (const [field, message] of Object.entries(fieldErrors(error))) {
        const key = field.toLowerCase();
        if (key in values) {
          password.setError(key as keyof PasswordValues, { message });
        }
      }

      setPasswordError(errorMessage(error));
    }
  }

  if (!user) {
    return null;
  }

  return (
    <div className="row g-4">
      <div className="col-12 col-lg-4">
        <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="account-summary">
          <h2 className="mp-section-title" id="account-summary" style={{ fontSize: "var(--fs-h3)" }}>
            Your account
          </h2>

          <p style={{ margin: 0, fontWeight: 600 }}>{user.fullName}</p>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>{user.email}</p>

          <div className="d-flex align-items-center flex-wrap mt-2" style={{ gap: "var(--space-2)" }}>
            <StatusBadge tone="info">{user.role}</StatusBadge>
            {user.role === "Seller" && user.sellerStatus ? <StatusBadge tone="warning">Seller {user.sellerStatus}</StatusBadge> : null}
          </div>

          <p style={{ margin: "var(--space-3) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
            Joined {formatDate(user.createdAt)}
            {user.lastLoginAt ? ` · last signed in ${formatDate(user.lastLoginAt)}` : ""}
          </p>

          {user.role === "Seller" ? (
            <a href="/seller" className="btn btn-sm btn-outline-secondary mt-3">
              Go to the seller dashboard
            </a>
          ) : null}

          <button type="button" className="btn btn-sm mt-3" onClick={() => void signOut()} style={{ color: "var(--text-subtle)" }}>
            Sign out of this browser
          </button>
        </section>
      </div>

      <div className="col-12 col-lg-8">
        <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="profile-form">
          <h2 className="mp-section-title" id="profile-form" style={{ fontSize: "var(--fs-h3)" }}>
            Your details
          </h2>

          <form onSubmit={profile.handleSubmit(onProfileSubmit)} noValidate className="mp-stack">
            {profileError ? (
              <p role="alert" className="mp-alert mp-alert-danger">
                {profileError}
              </p>
            ) : null}

            <div className="row g-2">
              <div className="col-12 col-sm-6">
                <TextField label="First name" autoComplete="given-name" required error={profile.formState.errors.firstName?.message} {...profile.register("firstName")} />
              </div>
              <div className="col-12 col-sm-6">
                <TextField label="Last name" autoComplete="family-name" required error={profile.formState.errors.lastName?.message} {...profile.register("lastName")} />
              </div>
            </div>

            <TextField
              label="Email"
              type="email"
              value={user.email}
              readOnly
              hint="The address you sign in with. It cannot be changed here."
              style={{ opacity: 0.7 }}
            />

            <TextField label="Phone" type="tel" autoComplete="tel" error={profile.formState.errors.phoneNumber?.message} {...profile.register("phoneNumber")} />

            <div>
              <button type="submit" className="btn btn-sm btn-primary" disabled={profile.formState.isSubmitting}>
                {profile.formState.isSubmitting ? "Saving…" : "Save details"}
              </button>
            </div>
          </form>
        </section>

        <section className="mp-card mt-3" style={{ padding: "var(--space-4)" }} aria-labelledby="password-form">
          <h2 className="mp-section-title" id="password-form" style={{ fontSize: "var(--fs-h3)" }}>
            Change your password
          </h2>
          <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            This signs you out everywhere, including here, straight afterwards.
          </p>

          <form onSubmit={password.handleSubmit(onPasswordSubmit)} noValidate className="mp-stack">
            {passwordError ? (
              <p role="alert" className="mp-alert mp-alert-danger">
                {passwordError}
              </p>
            ) : null}


            <TextField
              label="Current password"
              type="password"
              autoComplete="current-password"
              required
              error={password.formState.errors.currentPassword?.message}
              {...password.register("currentPassword")}
            />

            <div className="row g-2">
              <div className="col-12 col-sm-6">
                <TextField
                  label="New password"
                  type="password"
                  autoComplete="new-password"
                  required
                  error={password.formState.errors.newPassword?.message}
                  {...password.register("newPassword")}
                />
              </div>
              <div className="col-12 col-sm-6">
                <TextField
                  label="Confirm new password"
                  type="password"
                  autoComplete="new-password"
                  required
                  error={password.formState.errors.confirmPassword?.message}
                  {...password.register("confirmPassword")}
                />
              </div>
            </div>

            <div>
              <button type="submit" className="btn btn-sm btn-primary" disabled={password.formState.isSubmitting}>
                {password.formState.isSubmitting ? "Changing…" : "Change my password"}
              </button>
            </div>
          </form>
        </section>
      </div>
    </div>
  );
}

export function AccountPage() {
  return (
    <RequireAuth returnTo="/profile">
      <AccountPanel />
    </RequireAuth>
  );
}
