import type { ResendConfirmationRequest } from "@/lib/types";

export type ResendResult = "sent" | "rateLimited" | "checkFailed" | "failed";

/**
 * Asks the API to email a new confirmation link. It answers the same whether or not the address has an
 * account waiting, so "sent" only means the request went through.
 */
export async function resendConfirmation(email: string, turnstileToken: string | null): Promise<ResendResult> {
  try {
    const body: ResendConfirmationRequest = { email, turnstileToken };
    const res = await fetch("/api/auth/resend-confirmation", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    if (res.status === 429) return "rateLimited";
    // The API turned down the Turnstile token.
    if (res.status === 400) return "checkFailed";
    return res.ok ? "sent" : "failed";
  } catch {
    return "failed";
  }
}

/** The check-your-inbox page for an address. */
export function checkEmailPath(email: string): string {
  return `/check-email?email=${encodeURIComponent(email)}`;
}
