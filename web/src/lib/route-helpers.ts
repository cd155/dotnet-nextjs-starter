import * as Sentry from "@sentry/nextjs";
import { cookies, headers } from "next/headers";
import { getTranslations } from "@/lib/i18n/server";
import type { StaticMessageKey } from "@/lib/i18n/translate";

export const API_BASE = process.env.API_BASE_URL ?? "http://localhost:8080";

// The session is the ASP.NET Core Identity cookie the API issues at login. This app keeps it on its
// own origin under its own name, and presents it back to the API under the name the API issued.
//
// Browsers scope cookies by host, not port: on localhost, a session stored under the API's name
// would be shared with, and overwritten by, any other ASP.NET Core app you run locally. A name of
// its own keeps this one signed in beside them.
export const SESSION_COOKIE = "app_session";
export const BACKEND_AUTH_COOKIE = ".AspNetCore.Identity.Application";

export async function getSessionCookie() {
  const store = await cookies();
  return store.get(SESSION_COOKIE);
}

/**
 * Copies the session the API issued on login or registration onto this app's origin. Returns
 * whether the response carried one — if it did not, the sign-in failed silently on the backend.
 */
export async function setSessionFromBackend(backendRes: Response): Promise<boolean> {
  const cookieStore = await cookies();
  let found = false;

  for (const header of backendRes.headers.getSetCookie()) {
    if (header.startsWith(`${BACKEND_AUTH_COOKIE}=`)) {
      const value = header.substring(BACKEND_AUTH_COOKIE.length + 1).split(";")[0];
      cookieStore.set(SESSION_COOKIE, value, {
        httpOnly: true,
        secure: process.env.NODE_ENV === "production",
        sameSite: "lax",
        path: "/",
        maxAge: 7 * 24 * 60 * 60,
      });
      found = true;
    }
  }

  return found;
}

// Route handlers return `{ error }` bodies that are shown to the user verbatim
// (see the login form), so the message has to follow the reader's locale.
// Taking a catalog key rather than a string keeps each call site a one-liner
// and keeps the wording in the catalog with everything else.
export async function errorResponse(key: StaticMessageKey, status: number) {
  const { t } = await getTranslations();
  return Response.json({ error: t(key) }, { status });
}

// Identity's error codes for the password checks a user can fail, so the message follows the reader's locale.
const IDENTITY_ERRORS: Record<string, StaticMessageKey> = {
  PasswordMismatch: "accountSettings.wrongCurrentPassword",
  PasswordTooShort: "accountSettings.passwordTooShort",
  PasswordRequiresDigit: "accountSettings.passwordRequiresDigit",
  PasswordRequiresLower: "accountSettings.passwordRequiresLower",
  PasswordRequiresUpper: "accountSettings.passwordRequiresUpper",
};

/**
 * A validation problem's Identity errors, keyed by code, as one message for the user. A code without a catalog
 * key shows the API's own (English) text.
 */
export async function identityErrorMessage(errors: Record<string, string[]>): Promise<string> {
  const { t } = await getTranslations();
  return Object.entries(errors)
    .flatMap(([code, descriptions]) => (IDENTITY_ERRORS[code] ? [t(IDENTITY_ERRORS[code])] : descriptions))
    .join(" ");
}

/**
 * Relays the caller's IP to the backend.
 *
 * The backend's global rate limiter partitions by client IP, taken from `X-Forwarded-For`. Every
 * call from here arrives over the same connection, so without this header the whole user base
 * shares one bucket and one busy user rate-limits everyone else. The reverse proxy in front of
 * this app sets the header; we only pass it along. Absent in local development, where there is no
 * proxy — the backend then falls back to the connection's address, as before.
 */
export async function getForwardedForHeader(): Promise<Record<string, string>> {
  const forwarded = (await headers()).get("x-forwarded-for");
  return forwarded ? { "X-Forwarded-For": forwarded } : {};
}

/**
 * Forwards one of the account's anonymous requests (confirming an email, asking for a new link) to the API with the
 * caller's IP, and answers as their pages expect: an empty 204, the API's 400 for a link it refuses, or `{ error }`
 * in the reader's language.
 */
export async function forwardAnonymously(path: string, request: Request, context: string): Promise<Response> {
  let body: string;
  try {
    body = JSON.stringify(await request.json());
  } catch {
    return errorResponse("errors.invalidJsonBody", 400);
  }

  let backendRes: Response;
  try {
    backendRes = await fetch(`${API_BASE}${path}`, {
      method: "POST",
      signal: AbortSignal.timeout(15_000),
      cache: "no-store",
      headers: { "Content-Type": "application/json", ...(await getForwardedForHeader()) },
      body,
    });
  } catch (err) {
    Sentry.captureException(err, { tags: { context } });
    return errorResponse("errors.generic", 502);
  }
  // Unread, the body would hold the connection back from reuse.
  await backendRes.body?.cancel();

  if (backendRes.ok) return new Response(null, { status: 204 });
  if (backendRes.status === 429) return errorResponse("errors.rateLimit", 429);
  if (backendRes.status >= 500) {
    Sentry.captureException(new Error(`Backend error in ${context}: ${backendRes.status}`), {
      tags: { context },
      extra: { statusCode: backendRes.status },
    });
  }
  return errorResponse("errors.generic", backendRes.status);
}

/**
 * Forwards one of the pages' writes to the API as the session's user, with the caller's IP, and
 * answers as the pages expect: the API's JSON, an empty 204, or `{ error }` in the reader's language.
 * A 400 means a page built a request the API refuses, which is a bug worth seeing, so it's reported
 * along with the outages.
 */
export async function forwardToApi(
  path: string,
  { method, body, context }: { method: "POST" | "PUT" | "DELETE"; body?: string; context: string },
): Promise<Response> {
  const session = await getSessionCookie();
  if (!session) {
    return errorResponse("errors.notAuthenticated", 401);
  }

  let backendRes: Response;
  try {
    backendRes = await fetch(`${API_BASE}${path}`, {
      method,
      signal: AbortSignal.timeout(15_000),
      cache: "no-store",
      headers: {
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
        Cookie: `${BACKEND_AUTH_COOKIE}=${session.value}`,
        ...(await getForwardedForHeader()),
      },
      body,
    });
  } catch (err) {
    Sentry.captureException(err, { tags: { context } });
    return errorResponse("errors.generic", 502);
  }

  if (!backendRes.ok) {
    if (backendRes.status >= 500 || backendRes.status === 400) {
      Sentry.captureException(new Error(`Backend error in ${context}: ${backendRes.status}`), {
        tags: { context },
        extra: { statusCode: backendRes.status },
      });
    }
    if (backendRes.status === 401) return errorResponse("errors.sessionExpired", 401);
    if (backendRes.status === 429) return errorResponse("errors.rateLimit", 429);
    return errorResponse("errors.generic", backendRes.status);
  }

  if (backendRes.status === 204) return new Response(null, { status: 204 });
  return Response.json(await backendRes.json());
}
