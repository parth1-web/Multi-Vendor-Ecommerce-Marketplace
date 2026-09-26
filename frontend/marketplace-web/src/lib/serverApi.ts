/**
 * Server-side fetch for Server Components.
 *
 * This is deliberately not the axios client: a server render has no access token, no cookie
 * jar and no interceptor, and pretending otherwise would mean shipping a session token to the
 * server for data that is public. Anything authenticated is fetched on the client instead.
 */

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

/** Error carrying the status so a route can decide between a 404 page and a retry. */
export class ApiError extends Error {
  constructor(
    readonly path: string,
    readonly status: number,
    readonly body?: string,
  ) {
    super(`${path} responded ${status}`);
    this.name = "ApiError";
  }
}

/** Cache tag for a path, so a mutation can invalidate exactly the reads it affects. */
export function tagFor(path: string): string {
  const withoutQuery = path.split("?")[0];
  const segments = withoutQuery.split("/").filter(Boolean);

  return ["api", ...segments].join(":");
}

export async function serverGet<T>(path: string, revalidate = 60): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    next: { revalidate, tags: [tagFor(path)] },
    headers: { Accept: "application/json" },
  });

  if (!response.ok) {
    throw new ApiError(path, response.status, await response.text().catch(() => undefined));
  }

  return (await response.json()) as T;
}
