import * as Sentry from "@sentry/nextjs";
import { cookies } from "next/headers";
import {
  API_BASE,
  BACKEND_AUTH_COOKIE,
  SESSION_COOKIE,
  errorResponse,
  getForwardedForHeader,
} from "@/lib/route-helpers";

// Ends every session, for a lost computer: the API changes the user's security stamp, which ends every other
// cookie session at its next check. This one ends now.
export async function POST() {
  const cookieStore = await cookies();
  const session = cookieStore.get(SESSION_COOKIE);
  if (!session) {
    return errorResponse("errors.notAuthenticated", 401);
  }

  let backendRes: Response;
  try {
    backendRes = await fetch(`${API_BASE}/api/v1/account/sign-out-everywhere`, {
      method: "POST",
      signal: AbortSignal.timeout(10_000),
      headers: {
        Cookie: `${BACKEND_AUTH_COOKIE}=${session.value}`,
        ...(await getForwardedForHeader()),
      },
    });
  } catch (err) {
    Sentry.captureException(err, { tags: { context: "signOutEverywhere" } });
    return errorResponse("errors.generic", 502);
  }

  // A 401 means this session had already ended; either way, it goes.
  if (!backendRes.ok && backendRes.status !== 401) {
    if (backendRes.status >= 500) {
      Sentry.captureException(new Error(`Sign out everywhere backend error: ${backendRes.status}`), {
        tags: { context: "signOutEverywhere" },
        extra: { statusCode: backendRes.status },
      });
    }
    return errorResponse(backendRes.status === 429 ? "errors.rateLimit" : "errors.generic", backendRes.status);
  }

  cookieStore.delete(SESSION_COOKIE);
  return Response.json({ ok: true });
}
