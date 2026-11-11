import * as Sentry from "@sentry/nextjs";
import type { ChangePasswordRequest } from "@/lib/types";
import {
  API_BASE,
  BACKEND_AUTH_COOKIE,
  errorResponse,
  getForwardedForHeader,
  getSessionCookie,
  identityErrorMessage,
  setSessionFromBackend,
} from "@/lib/route-helpers";

export async function POST(request: Request) {
  const session = await getSessionCookie();
  if (!session) {
    return errorResponse("errors.notAuthenticated", 401);
  }

  let body: ChangePasswordRequest;
  try {
    body = await request.json();
  } catch {
    return errorResponse("errors.invalidJsonBody", 400);
  }

  let backendRes: Response;
  try {
    backendRes = await fetch(`${API_BASE}/api/v1/account/change-password`, {
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
    Sentry.captureException(err, { tags: { context: "change-password" } });
    return errorResponse("errors.generic", 502);
  }

  if (!backendRes.ok) {
    if (backendRes.status === 429) return errorResponse("errors.rateLimit", 429);
    if (backendRes.status === 401) return errorResponse("errors.sessionExpired", 401);
    if (backendRes.status >= 500) {
      Sentry.captureException(new Error(`Change password backend error: ${backendRes.status}`), {
        tags: { context: "change-password" },
        extra: { statusCode: backendRes.status },
      });
    }

    // Safely parse the backend response — it may not be JSON (e.g. 503 from proxy)
    let data: Record<string, unknown> = {};
    try {
      data = await backendRes.json();
    } catch {
      // Non-JSON body; fall through to the generic message
    }

    if (data?.errors) {
      const error = await identityErrorMessage(data.errors as Record<string, string[]>);
      return Response.json({ error }, { status: backendRes.status });
    }
    return errorResponse("accountSettings.changeFailed", backendRes.status);
  }

  // The change rotated the security stamp; the API's new cookie keeps this session signed in.
  await setSessionFromBackend(backendRes);

  return new Response(null, { status: 204 });
}
