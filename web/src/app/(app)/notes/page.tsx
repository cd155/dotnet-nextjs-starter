import type { Metadata } from "next";
import { NotesBoard } from "@/components/notes/notes-board";
import { getNotes } from "@/lib/api";
import { getTranslations } from "@/lib/i18n/server";
import type { Note } from "@/lib/types";

export async function generateMetadata(): Promise<Metadata> {
  const { t } = await getTranslations("notes");
  return { title: t("metaTitle") };
}

// The signed-in user's notes: read here on the server with the session cookie, written through the /api/notes
// route handlers, after which the page refreshes.
export default async function NotesPage() {
  const { t } = await getTranslations("notes");

  let notes: Note[] | null;
  try {
    notes = await getNotes();
  } catch {
    // getNotes reports an outage itself.
    notes = null;
  }

  return (
    <div className="mx-auto w-full max-w-2xl space-y-6">
      <h1 className="text-2xl font-bold">{t("title")}</h1>
      {notes === null ? <p className="text-muted">{t("unavailable")}</p> : <NotesBoard notes={notes} />}
    </div>
  );
}
