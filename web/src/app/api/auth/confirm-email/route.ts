import { forwardAnonymously } from "@/lib/route-helpers";

// The confirm-email page posts the link's user ID and code. A 400 is an expired or invalid link.
export async function POST(request: Request) {
  return forwardAnonymously("/api/v1/account/confirm-email", request, "confirm-email");
}
