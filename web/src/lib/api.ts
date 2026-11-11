import * as Sentry from "@sentry/nextjs";
import { cookies, headers } from "next/headers";
import { ApiError } from "@/lib/errors";
import type { Note, UserProfile } from "@/lib/types";
import { API_BASE, BACKEND_AUTH_COOKIE, SESSION_COOKIE } from "@/lib/route-helpers";

/**
 * Headers forwarded to the backend on a per-session request: the session cookie, and the caller's IP.
 *
 * The backend's global rate limiter partitions by client IP, taken from `X-Forwarded-For`. Every
 * call from this server arrives over the same connection, so without relaying that header the
 * whole user base shares one bucket and one busy user rate-limits everyone else. The reverse proxy
 * sets it; we only pass it along, and it is absent in local development.
 *
 * Deliberately not folded into `apiFetch`: reading headers/cookies makes a request dynamic, and
 * public fetchers should be able to rely on `next: { revalidate }` caching. Only the authenticated
 * calls, which are already dynamic and `no-store`, go through here.
 */
async function getAuthCookieHeader(): Promise<Record<string, string>> {
  const [cookieStore, headerStore] = await Promise.all([cookies(), headers()]);
  const result: Record<string, string> = {};

  const session = cookieStore.get(SESSION_COOKIE);
  if (session) {
    result.Cookie = `${BACKEND_AUTH_COOKIE}=${session.value}`;
  }

  const forwarded = headerStore.get("x-forwarded-for");
  if (forwarded) {
    result["X-Forwarded-For"] = forwarded;
  }

  return result;
}

function isCritical(err: unknown): boolean {
  if (!(err instanceof ApiError)) return true;
  // 400 captured: SSR requests are app-generated, so a 400 means a bug in our code
  // 401/404/429 skipped: session expired, no data yet, and rate-limited are all expected
  return err.status === 400 || (err.status !== 401 && err.status !== 404 && err.status !== 429 && err.status >= 500);
}

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE}${path}`, {
      ...options,
      headers: {
        "Content-Type": "application/json",
        ...options?.headers,
      },
    });
  } catch (err) {
    // No response at all: the backend is down or unreachable. A 503 lets pages show their
    // "unavailable" state instead of crashing.
    throw new ApiError(503, "Service Unavailable", { cause: err });
  }

  if (!res.ok) {
    throw new ApiError(res.status, res.statusText);
  }

  return res.json();
}

// An authenticated fetcher, as the session's user, never cached. Allowed to throw so a page can tell an
// outage from an empty list and surface the ApiError status .
// Reported first, since the page only renders the error state.
async function authenticated<T>(path: string, context: string): Promise<T> {
  const authHeaders = await getAuthCookieHeader();
  try {
    return await apiFetch<T>(path, { cache: "no-store", headers: authHeaders });
  } catch (err) {
    if (isCritical(err)) Sentry.captureException(err, { tags: { context } });
    throw err;
  }
}

/** The signed-in user's notes, pinned first, then the most recently changed. */
export function getNotes(): Promise<Note[]> {
  return authenticated("/api/v1/me/notes", "getNotes");
}

/** One of the signed-in user's notes. A 404 means it isn't theirs, or doesn't exist. */
export function getNote(id: number): Promise<Note> {
  return authenticated(`/api/v1/me/notes/${id}`, "getNote");
}

/** The signed-in user. */
export function getProfile(): Promise<UserProfile> {
  return authenticated("/api/v1/account/profile", "getProfile");
}
