import { forwardToApi } from "@/lib/route-helpers";

// POST /api/notes: creates a note as the session's user. The page's writes come through this server, which holds
// the session cookie; the browser never calls the API itself.
export async function POST(request: Request) {
  return forwardToApi("/api/v1/me/notes", { method: "POST", body: await request.text(), context: "createNote" });
}
