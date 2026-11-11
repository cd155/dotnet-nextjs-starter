import * as Sentry from "@sentry/nextjs";
import type { RegisterRequest } from "@/lib/types";
import { API_BASE, errorResponse, getForwardedForHeader } from "@/lib/route-helpers";

export async function POST(request: Request) {
  let body: RegisterRequest;
  try {
    body = await request.json();
  } catch {
    return errorResponse("errors.invalidJsonBody", 400);
  }

  const backendRes = await fetch(`${API_BASE}/api/v1/account/register`, {
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
    if (backendRes.status === 429) {
      return errorResponse("errors.rateLimit", 429);
    }
    if (backendRes.status >= 500) {
      Sentry.captureException(
        new Error(`Register backend error: ${backendRes.status}`),
        { tags: { context: "register" }, extra: { statusCode: backendRes.status } }
      );
    }
    // Extract validation errors from ASP.NET problem details
    if (data?.errors) {
      const messages = Object.values(
        data.errors as Record<string, string[]>
      ).flat();
      return Response.json(
        { error: messages.join(" ") },
        { status: backendRes.status }
      );
    }
    const message =
      (data?.detail as string) ||
      (data?.title as string) ||
      `Registration failed (${backendRes.status})`;
    return Response.json({ error: message }, { status: backendRes.status });
  }

  // No session yet: the user signs in once the emailed link confirms the address.
  return new Response(null, { status: 204 });
}
