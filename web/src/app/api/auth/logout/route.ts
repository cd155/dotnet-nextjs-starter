import * as Sentry from "@sentry/nextjs";
import { cookies } from "next/headers";
import { API_BASE, BACKEND_AUTH_COOKIE, SESSION_COOKIE, getForwardedForHeader } from "@/lib/route-helpers";

export async function POST() {
  const cookieStore = await cookies();
  const session = cookieStore.get(SESSION_COOKIE);

  // Forward logout to backend if we have a session
  if (session) {
    try {
      const backendRes = await fetch(`${API_BASE}/api/v1/account/logout`, {
        method: "POST",
        headers: {
          Cookie: `${BACKEND_AUTH_COOKIE}=${session.value}`,
          ...(await getForwardedForHeader()),
        },
      });
      // fetch resolves on a 5xx, so the catch below never sees a broken backend.
      if (backendRes.status >= 500) {
        Sentry.captureException(
          new Error(`Logout backend error: ${backendRes.status}`),
          { tags: { context: "logout" }, extra: { statusCode: backendRes.status } }
        );
      }
    } catch (err) {
      Sentry.captureException(err, { tags: { context: "logout" } });
      // Backend logout failed — still clear the frontend cookie
    }
  }

  // Clear the frontend session
  cookieStore.delete(SESSION_COOKIE);

  return Response.json({ ok: true });
}
