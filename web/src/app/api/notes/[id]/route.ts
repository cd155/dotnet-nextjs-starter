import { forwardToApi } from "@/lib/route-helpers";

type Context = { params: Promise<{ id: string }> };

// PUT and DELETE /api/notes/{id}, as the session's user. The API answers 404 for someone else's note.
export async function PUT(request: Request, { params }: Context) {
  const { id } = await params;
  return forwardToApi(`/api/v1/me/notes/${encodeURIComponent(id)}`, {
    method: "PUT",
    body: await request.text(),
    context: "updateNote",
  });
}

export async function DELETE(_request: Request, { params }: Context) {
  const { id } = await params;
  return forwardToApi(`/api/v1/me/notes/${encodeURIComponent(id)}`, { method: "DELETE", context: "deleteNote" });
}
