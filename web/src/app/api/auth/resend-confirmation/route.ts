import { forwardAnonymously } from "@/lib/route-helpers";

// The API answers the same whether or not the address has an account waiting.
export async function POST(request: Request) {
  return forwardAnonymously("/api/v1/account/resend-confirmation", request, "resend-confirmation");
}
