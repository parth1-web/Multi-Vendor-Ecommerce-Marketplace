/**
 * The browser's API client, and the only place a token is ever held.
 *
 * Three rules shape this file:
 *
 * 1. The access token lives in a module singleton, never in localStorage. Anything on disk is
 *    readable by any script on the page; the refresh token is already an HttpOnly cookie, and
 *    putting the access token beside it would undo that.
 * 2. A 401 triggers exactly one refresh. Concurrent 401s queue behind that single attempt
 *    rather than each firing their own, which is how a refresh loop starts.
 * 3. A failure clears the session and sends the user to sign in, keeping where they were.
 */

import axios, { AxiosError, AxiosInstance, AxiosRequestConfig, InternalAxiosRequestConfig } from "axios";

import { ApiErrorShape, ProblemDetails } from "@/types/api";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

/** Injected by the request interceptor. Set by the auth store once a session exists. */
type TokenReader = () => string | null;

/** Called when a refresh fails for good, so the app can drop the session. */
type SessionInvalidator = () => void;

let readToken: TokenReader = () => null;
let invalidateSession: SessionInvalidator = () => undefined;

/** Paths that must never carry an Authorization header or trigger a refresh. */
const anonymousPaths = ["/api/auth/login", "/api/auth/register", "/api/auth/refresh", "/api/auth/logout"];

export function configureApi(options: { readToken: TokenReader; invalidateSession: SessionInvalidator }): void {
  readToken = options.readToken;
  invalidateSession = options.invalidateSession;
}

export const apiClient: AxiosInstance = axios.create({
  baseURL: API_URL,
  timeout: 30_000,
  headers: { Accept: "application/json" },
  withCredentials: true,
});

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = readToken();

  if (token && !isAnonymous(config.url)) {
    config.headers.set("Authorization", `Bearer ${token}`);
  }

  // The API answers with problem+json; asking for it keeps error bodies consistent.
  config.headers.set("Accept", "application/json, application/problem+json");

  return config;
});

/**
 * The single-flight refresh.
 *
 * `refreshPromise` is the whole mechanism: the first 401 starts the refresh and every other
 * 401 that arrives while it is in flight awaits the same promise, then replays its request.
 */
let refreshPromise: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
  try {
    // A bare client: the axios instance would attach a token to the refresh itself and recurse.
    const response = await axios.post<{ accessToken: string }>(
      `${API_URL}/api/auth/refresh`,
      {},
      { withCredentials: true, headers: { Accept: "application/json" } },
    );

    return response.data.accessToken;
  } catch {
    return null;
  }
}

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ProblemDetails>) => {
    const original = error.config as (AxiosRequestConfig & { _retried?: boolean }) | undefined;
    const status = error.response?.status;

    if (status !== 401 || !original || original._retried || isAnonymous(original.url)) {
      return Promise.reject(toApiError(error));
    }

    // One attempt at a time, however many requests are waiting.
    refreshPromise ??= refreshAccessToken().finally(() => {
      refreshPromise = null;
    });

    const token = await refreshPromise;

    if (!token) {
      // Clear the session and let the app route. A hard navigation from here would throw
      // away every in-memory cache on the way, and a 401 on a background poll should not
      // hijack whatever the user is reading.
      invalidateSession();

      return Promise.reject(toApiError(error));
    }

    original._retried = true;
    original.headers = { ...original.headers, Authorization: `Bearer ${token}` };

    return apiClient.request(original);
  },
);

/** Normalises anything axios can throw into one error shape the UI can render. */
export function toApiError(error: unknown): ApiErrorShape {
  if (axios.isAxiosError<ProblemDetails>(error)) {
    const body = error.response?.data;
    const fieldErrors = body?.errors
      ? Object.fromEntries(Object.entries(body.errors).map(([key, messages]) => [key, Array.isArray(messages) ? messages : [String(messages)]]))
      : undefined;

    return {
      status: error.response?.status ?? 0,
      type: body?.type,
      title: body?.title ?? "Something went wrong",
      detail: body?.detail ?? error.message,
      fieldErrors,
      correlationId: (body?.extensions?.correlationId as string | undefined) ?? error.response?.headers?.["x-correlation-id"],
    };
  }

  return {
    status: 0,
    title: "Something went wrong",
    detail: error instanceof Error ? error.message : "An unexpected error occurred.",
  };
}

function isAnonymous(url: string | undefined): boolean {
  if (!url) {
    return true;
  }

  return anonymousPaths.some((path) => url.includes(path));
}
