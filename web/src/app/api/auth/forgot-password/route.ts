import { forwardAnonymously } from "@/lib/route-helpers";

// The API answers the same whether or not the address has an account, or is past its own limit.
export async function POST(request: Request) {
  return forwardAnonymously("/api/v1/account/forgot-password", request, "forgot-password");
}
