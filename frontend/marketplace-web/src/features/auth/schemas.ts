/**
 * The shapes the sign-in and registration forms hold.
 *
 * They live apart from the API types on purpose: a form has a password and a confirmation,
 * and neither belongs in a request body. Sharing the two is how a password ends up in a type
 * that also describes a logged-in user.
 */

import { z } from "zod";

/**
 * The password rules, once.
 *
 * The API enforces the same set, and this copy exists so the form can say what is wrong before
 * a round trip. Duplicated rules drift, so both sides are asserted against each other in the
 * integration suite rather than trusted.
 */
export const passwordRules = z
  .string()
  .min(8, "Use at least 8 characters.")
  .regex(/[A-Z]/, "Include an upper-case letter.")
  .regex(/[a-z]/, "Include a lower-case letter.")
  .regex(/[0-9]/, "Include a digit.");

export const emailRules = z.string().trim().min(1, "Enter your email address.").email("Enter a valid email address.");

export const loginSchema = z.object({
  email: emailRules,
  password: z.string().min(1, "Enter your password."),
});

export const registerSchema = z
  .object({
    firstName: z.string().trim().min(1, "Enter your first name.").max(100, "That is longer than we allow."),
    lastName: z.string().trim().min(1, "Enter your last name.").max(100, "That is longer than we allow."),
    email: emailRules,
    phoneNumber: z
      .string()
      .trim()
      .regex(/^[+0-9 ()-]{6,20}$/, "Enter a valid phone number.")
      .optional()
      .or(z.literal("")),
    password: passwordRules,
    confirmPassword: z.string().min(1, "Confirm your password."),
    // The choice is a checkbox rather than a hidden field: a seller account and a customer
    // account are different products, and the difference should be visible before signing up.
    wantsToSell: z.boolean(),
    // A checkbox that has to end up true, but which starts out false, so the type stays
    // boolean: a literal true would make the unchecked default unrepresentable.
    terms: z.boolean().refine(accepted => accepted, { message: "Please accept the terms to continue." }),
  })
  .refine((values) => values.password === values.confirmPassword, {
    message: "The passwords do not match.",
    path: ["confirmPassword"],
  });

export const forgotPasswordSchema = z.object({ email: emailRules });

export const resetPasswordSchema = z
  .object({
    password: passwordRules,
    confirmPassword: z.string().min(1, "Confirm your new password."),
  })
  .refine((values) => values.password === values.confirmPassword, {
    message: "The passwords do not match.",
    path: ["confirmPassword"],
  });

export type LoginValues = z.infer<typeof loginSchema>;
export type RegisterValues = z.infer<typeof registerSchema>;
export type ForgotPasswordValues = z.infer<typeof forgotPasswordSchema>;
export type ResetPasswordValues = z.infer<typeof resetPasswordSchema>;
