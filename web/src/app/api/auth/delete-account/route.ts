import * as Sentry from "@sentry/nextjs";
import { cookies } from "next/headers";
import type { DeleteAccountRequest } from "@/lib/types";
import {
  API_BASE,
  BACKEND_AUTH_COOKIE,
  SESSION_COOKIE,
  errorResponse,
  getForwardedForHeader,
} from "@/lib/route-helpers";

// Deletes the signed-in user's account, then clears the session cookie, which the account no longer backs.
export async function POST(request: Request) {
  const cookieStore = await cookies();
  const session = cookieStore.get(SESSION_COOKIE);
  if (!session) {
    return errorResponse("errors.notAuthenticated", 401);
  }

  let body: DeleteAccountRequest;
  try {
    body = await request.json();
  } catch {
    return errorResponse("errors.invalidJsonBody", 400);
  }

  let backendRes: Response;
  try {
    backendRes = await fetch(`${API_BASE}/api/v1/account/delete`, {
      method: "POST",
      signal: AbortSignal.timeout(10_000),
      headers: {
        "Content-Type": "application/json",
        Cookie: `${BACKEND_AUTH_COOKIE}=${session.value}`,
        ...(await getForwardedForHeader()),
      },
      body: JSON.stringify(body),
    });
  } catch (err) {
    Sentry.captureException(err, { tags: { context: "deleteAccount" } });
    return errorResponse("errors.generic", 502);
  }

  if (!backendRes.ok) {
    switch (backendRes.status) {
      case 400:
        return errorResponse("accountSettings.deleteWrongPasswordOrCode", 400);
      case 401:
        return errorResponse("errors.sessionExpired", 401);
      case 409:
        return errorResponse("accountSettings.deleteBlocked", 409);
      case 423:
        return errorResponse("accountSettings.deleteLocked", 423);
      case 429:
        return errorResponse("errors.rateLimit", 429);
    }

    if (backendRes.status >= 500) {
      Sentry.captureException(new Error(`Delete account backend error: ${backendRes.status}`), {
        tags: { context: "deleteAccount" },
        extra: { statusCode: backendRes.status },
      });
    }
    return errorResponse("accountSettings.deleteFailed", backendRes.status);
  }

  cookieStore.delete(SESSION_COOKIE);
  return new Response(null, { status: 204 });
}
