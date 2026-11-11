"use client";

import { Pencil, Pin, Trash2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { ConfirmSheet } from "@/components/confirm-sheet";
import { useErrorToast } from "@/components/error-toast";
import { Button } from "@/components/ui/button";
import { useTranslations } from "@/lib/i18n/client";
import type { Note, SaveNoteRequest } from "@/lib/types";
import { cn } from "@/lib/utils";

/** Sends one write through this app's route handlers, and throws on a failure with the handler's message. */
async function write(url: string, method: "POST" | "PUT" | "DELETE", body?: SaveNoteRequest): Promise<void> {
  const response = await fetch(url, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    const data = (await response.json().catch(() => ({}))) as { error?: string };
    throw new Error(data.error ?? response.statusText);
  }
}

/** The list of notes, with a form for a new one at the top. Each note edits in place. */
export function NotesBoard({ notes }: { notes: Note[] }) {
  const t = useTranslations("notes");
  const router = useRouter();

  async function create(note: SaveNoteRequest) {
    await write("/api/notes", "POST", note);
    router.refresh();
  }

  return (
    <div className="space-y-6">
      <section className="rounded-2xl border border-card-border bg-card-bg p-4">
        <h2 className="mb-3 text-sm font-bold text-muted/80">{t("newNote")}</h2>
        <NoteForm submitLabel={t("add")} onSave={create} resetAfterSave />
      </section>

      {notes.length === 0 ? (
        <p className="text-center text-sm text-muted">{t("empty")}</p>
      ) : (
        <ul className="space-y-3">
          {notes.map((note) => (
            <li key={note.id}>
              <NoteCard note={note} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function NoteCard({ note }: { note: Note }) {
  const t = useTranslations("notes");
  const tAll = useTranslations();
  const router = useRouter();
  const errors = useErrorToast();
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);

  async function update(values: SaveNoteRequest) {
    await write(`/api/notes/${note.id}`, "PUT", values);
    setEditing(false);
    router.refresh();
  }

  async function remove() {
    await write(`/api/notes/${note.id}`, "DELETE");
    setDeleting(false);
    router.refresh();
  }

  async function togglePin() {
    try {
      await write(`/api/notes/${note.id}`, "PUT", { title: note.title, body: note.body, isPinned: !note.isPinned });
      router.refresh();
    } catch (error) {
      errors.show(error);
    }
  }

  if (editing) {
    return (
      <div className="rounded-2xl border border-primary/40 bg-card-bg p-4">
        <NoteForm initial={note} submitLabel={t("save")} onSave={update} onCancel={() => setEditing(false)} />
      </div>
    );
  }

  const changed = new Date(note.updatedTime ?? note.createdTime);

  return (
    <article className="rounded-2xl border border-card-border bg-card-bg p-4">
      <div className="flex items-start gap-3">
        <h2 className="min-w-0 flex-1 font-semibold break-words">{note.title}</h2>
        <div className="flex shrink-0 items-center gap-1">
          <IconAction label={t("pin")} pressed={note.isPinned} onClick={() => void togglePin()}>
            <Pin className={note.isPinned ? "size-4 fill-current" : "size-4"} aria-hidden />
          </IconAction>
          <IconAction label={t("editNamed", { title: note.title })} onClick={() => setEditing(true)}>
            <Pencil className="size-4" aria-hidden />
          </IconAction>
          <IconAction label={t("deleteNamed", { title: note.title })} onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" aria-hidden />
          </IconAction>
        </div>
      </div>
      {note.body && <p className="mt-2 text-sm whitespace-pre-wrap break-words text-foreground/80">{note.body}</p>}
      <p className="mt-3 text-xs text-muted">
        {t("changed", { date: changed.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" }) })}
      </p>

      <ConfirmSheet
        isOpen={deleting}
        onClose={() => setDeleting(false)}
        title={t("deleteTitle")}
        body={t("deleteBody", { title: note.title })}
        confirmLabel={t("delete")}
        cancelLabel={tAll("common.cancel")}
        danger
        onConfirm={remove}
      />
    </article>
  );
}

function IconAction({
  label,
  pressed,
  onClick,
  children,
}: {
  label: string;
  pressed?: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      aria-pressed={pressed}
      onClick={onClick}
      className={cn(
        "rounded-full p-2 transition-colors hover:bg-card-border/70",
        pressed ? "text-primary" : "text-muted hover:text-foreground",
      )}
    >
      {children}
    </button>
  );
}

interface NoteFormProps {
  initial?: Note;
  submitLabel: string;
  onSave: (note: SaveNoteRequest) => Promise<void>;
  onCancel?: () => void;
  /** Empties the form after a save, for the new-note form. */
  resetAfterSave?: boolean;
}

function NoteForm({ initial, submitLabel, onSave, onCancel, resetAfterSave }: NoteFormProps) {
  const t = useTranslations("notes");
  const tAll = useTranslations();
  const [title, setTitle] = useState(initial?.title ?? "");
  const [body, setBody] = useState(initial?.body ?? "");
  const [isPinned, setIsPinned] = useState(initial?.isPinned ?? false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!title.trim()) {
      setError(t("titleRequired"));
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await onSave({ title, body, isPinned });
      if (resetAfterSave) {
        setTitle("");
        setBody("");
        setIsPinned(false);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }

  const fieldClasses =
    "w-full rounded-xl border border-card-border bg-background px-3 py-2 text-sm outline-none focus:border-primary";

  return (
    <form onSubmit={(event) => void submit(event)} className="space-y-3">
      <input
        aria-label={t("titleLabel")}
        placeholder={t("titleLabel")}
        value={title}
        maxLength={200}
        onChange={(event) => setTitle(event.target.value)}
        className={fieldClasses}
      />
      <textarea
        aria-label={t("bodyLabel")}
        placeholder={t("bodyLabel")}
        value={body}
        maxLength={10_000}
        rows={4}
        onChange={(event) => setBody(event.target.value)}
        className={fieldClasses}
      />
      {error && (
        <p role="alert" className="text-sm text-red-accent">
          {error}
        </p>
      )}
      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={isPinned} onChange={(event) => setIsPinned(event.target.checked)} />
          {t("pinned")}
        </label>
        <div className="ml-auto flex gap-2">
          {onCancel && (
            <Button variant="outline" onClick={onCancel}>
              {tAll("common.cancel")}
            </Button>
          )}
          <Button type="submit" disabled={busy}>
            {busy ? t("saving") : submitLabel}
          </Button>
        </div>
      </div>
    </form>
  );
}
