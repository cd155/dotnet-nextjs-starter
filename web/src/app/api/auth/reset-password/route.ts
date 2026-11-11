import * as Sentry from "@sentry/nextjs";
import type { ResetPasswordRequest } from "@/lib/types";
import { getTranslations } from "@/lib/i18n/server";
import { API_BASE, errorResponse, getForwardedForHeader, identityErrorMessage } from "@/lib/route-helpers";

// Its own handler rather than forwardAnonymously, which would show a refused password as a generic error. It
// doesn't sign in: the user logs in with the new password.
export async function POST(request: Request) {
  let body: ResetPasswordRequest;
  try {
    body = await request.json();
  } catch {
    return errorResponse("errors.invalidJsonBody", 400);
  }

  let backendRes: Response;
  try {
    backendRes = await fetch(`${API_BASE}/api/v1/account/reset-password`, {
      method: "POST",
      signal: AbortSignal.timeout(10_000),
      headers: {
        "Content-Type": "application/json",
        ...(await getForwardedForHeader()),
      },
      body: JSON.stringify(body),
    });
  } catch (err) {
    Sentry.captureException(err, { tags: { context: "reset-password" } });
    return errorResponse("errors.generic", 502);
  }

  if (backendRes.ok) {
    return new Response(null, { status: 204 });
  }
  if (backendRes.status === 429) {
    return errorResponse("errors.rateLimit", 429);
  }
  if (backendRes.status >= 500) {
    Sentry.captureException(new Error(`Reset password backend error: ${backendRes.status}`), {
      tags: { context: "reset-password" },
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

  // A refused password is a validation problem with Identity's codes; any other 400 is the link itself.
  if (data?.errors) {
    const error = await identityErrorMessage(data.errors as Record<string, string[]>);
    return Response.json({ error }, { status: backendRes.status });
  }
  if (backendRes.status === 400) {
    const { t } = await getTranslations();
    return Response.json({ error: t("passwordReset.invalid"), invalidLink: true }, { status: 400 });
  }
  return errorResponse("passwordReset.resetFailed", backendRes.status);
}
