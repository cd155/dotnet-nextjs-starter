import * as Sentry from "@sentry/nextjs";
import { cookies } from "next/headers";
import {
  API_BASE,
  BACKEND_AUTH_COOKIE,
  SESSION_COOKIE,
  errorResponse,
  getForwardedForHeader,
  getSessionCookie,
} from "@/lib/route-helpers";

export async function GET() {
  const session = await getSessionCookie();

  if (!session) {
    return errorResponse("errors.notAuthenticated", 401);
  }

  const backendRes = await fetch(`${API_BASE}/api/v1/account/profile`, {
    headers: {
      Cookie: `${BACKEND_AUTH_COOKIE}=${session.value}`,
      ...(await getForwardedForHeader()),
    },
    cache: "no-store",
  });

  if (!backendRes.ok) {
    // The API no longer accepts this session: it expired, or the API's data-protection keys
    // changed (locally, on every rebuild of its container). Drop it. The proxy treats any session
    // cookie as signed in, so a stale one would keep bouncing /login back to /notes. AuthProvider
    // calls this route on every page load, so the cookie is cleared before the user can get stuck.
    if (backendRes.status === 401) {
      (await cookies()).delete(SESSION_COOKIE);
      return errorResponse("errors.notAuthenticated", 401);
    }
    if (backendRes.status >= 500) {
      Sentry.captureException(
        new Error(`Profile backend error: ${backendRes.status}`),
        { tags: { context: "profile" }, extra: { statusCode: backendRes.status } }
      );
    }
    return errorResponse(
      backendRes.status === 429 ? "errors.rateLimit" : "errors.generic",
      backendRes.status
    );
  }

  const data = await backendRes.json();
  return Response.json(data);
}
