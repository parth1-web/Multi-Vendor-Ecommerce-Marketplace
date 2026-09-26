/**
 * Reading a failure the API actually sent.
 *
 * Every failure reaches a component as one `ApiErrorShape`, and a form can do better with it
 * than "something went wrong": the API names the fields it objected to, and showing that is
 * the difference between "your password is too short" and a message nobody can act on.
 */

import type { ApiErrorShape } from "@/types/api";

/** The most useful single line in a failure: what the API said, or a safe default. */
export function errorMessage(error: unknown, fallback = "Something went wrong. Please try again."): string {
  const shape = asApiError(error);

  if (shape) {
    if (shape.detail && shape.detail !== shape.title) {
      return shape.detail;
    }

    const firstFieldMessage = shape.fieldErrors ? Object.values(shape.fieldErrors).flat().find(Boolean) : undefined;
    if (firstFieldMessage) {
      return firstFieldMessage;
    }

    // A bare "One or more validation errors occurred." tells a person nothing, so it is only
    // used when there is nothing better at all.
    if (shape.title && shape.title !== "One or more validation errors occurred.") {
      return shape.title;
    }
  }

  if (typeof error === "string" && error.trim().length > 0) {
    return error;
  }

  if (error instanceof Error && error.message) {
    return error.message;
  }

  return fallback;
}

/**
 * Per-field messages keyed in lower case.
 *
 * The API names fields in the shape's own casing while a form registers them in camelCase, so
 * the keys are compared without regard to case rather than pasted into inputs that do not
 * have them.
 */
export function fieldErrors(error: unknown): Record<string, string> {
  const shape = asApiError(error);
  const result: Record<string, string> = {};

  for (const [field, messages] of Object.entries(shape?.fieldErrors ?? {})) {
    const message = Array.isArray(messages) ? messages.find(Boolean) : messages;

    if (message) {
      result[field.toLowerCase()] = message;
    }
  }

  return result;
}

/** True when the failure is the API refusing credentials rather than refusing the request. */
export function isSignInFailure(error: unknown): boolean {
  const status = asApiError(error)?.status;
  return status === 401 || status === 403;
}

function asApiError(error: unknown): ApiErrorShape | null {
  if (typeof error === "object" && error !== null && "status" in error && "title" in error) {
    return error as ApiErrorShape;
  }

  return null;
}
