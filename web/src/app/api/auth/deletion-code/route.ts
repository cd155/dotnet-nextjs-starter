import { forwardToApi } from "@/lib/route-helpers";

// Emails the signed-in user the code that, with their password, deletes the account.
export async function POST() {
  return forwardToApi("/api/v1/account/deletion-code", { method: "POST", context: "deletionCode" });
}
