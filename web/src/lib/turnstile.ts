import { connection } from "next/server";

/**
 * Cloudflare Turnstile's site key, for the forms that make the API send a confirmation email. Read per request,
 * so the built image works in any environment. Null without one (local development), and the forms show no
 * check: the API skips it too in Development without a secret key.
 */
export async function turnstileSiteKey(): Promise<string | null> {
  await connection();
  return process.env.TURNSTILE_SITE_KEY || null;
}
