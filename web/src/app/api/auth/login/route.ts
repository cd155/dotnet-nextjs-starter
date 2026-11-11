import * as Sentry from "@sentry/nextjs";
import { API_BASE, errorResponse, getForwardedForHeader, setSessionFromBackend } from "@/lib/route-helpers";

export async function POST(request: Request) {
  let body: { email: string; password: string };
  try {
    body = await request.json();
  } catch {
    return errorResponse("errors.invalidJsonBody", 400);
  }

  const backendRes = await fetch(`${API_BASE}/api/v1/account/login`, {
    method: "POST",
    signal: AbortSignal.timeout(10_000),
    headers: { "Content-Type": "application/json", ...(await getForwardedForHeader()) },
    body: JSON.stringify(body),
  });

  // Safely parse the backend response — it may not be JSON (e.g. 503 from proxy)
  let data: Record<string, unknown> = {};
  try {
    data = await backendRes.json();
  } catch {
    // Non-JSON body; leave data empty and fall through to status check
  }

  if (!backendRes.ok) {
    // The password was right, but the email waits for confirmation; the login form offers to resend the link.
    if (backendRes.status === 403) {
      return errorResponse("emailConfirmation.notConfirmed", 403);
    }
    if (backendRes.status === 429) {
      return errorResponse("errors.rateLimit", 429);
    }
    if (backendRes.status >= 500) {
      Sentry.captureException(
        new Error(`Login backend error: ${backendRes.status}`),
        { tags: { context: "login" }, extra: { statusCode: backendRes.status } }
      );
    }
    const message =
      (data?.detail as string) ||
      (data?.title as string) ||
      `Login failed (${backendRes.status})`;
    return Response.json({ error: message }, { status: backendRes.status });
  }

  // The session cookie must be present — if the backend didn't set one, login failed silently
  if (!(await setSessionFromBackend(backendRes))) {
    return errorResponse("errors.loginNoSession", 500);
  }

  return Response.json(data);
}
