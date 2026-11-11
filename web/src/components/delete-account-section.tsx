"use client";

import { useState } from "react";
import type { DeleteAccountRequest } from "@/lib/types";
import { useAuth } from "@/contexts/auth-context";
import { useTranslations } from "@/lib/i18n/client";

const inputClass =
  "w-full rounded-2xl border border-card-border bg-card-bg px-4 py-3 text-sm outline-none transition-colors focus:border-primary";

/**
 * Says what deleting the account removes, emails a code, then deletes the account with the code and the password.
 */
export function DeleteAccountSection() {
  const t = useTranslations("accountSettings");
  const tErrors = useTranslations("errors");
  const { logout } = useAuth();

  const [codeSent, setCodeSent] = useState(false);
  const [sending, setSending] = useState(false);
  const [currentPassword, setCurrentPassword] = useState("");
  const [code, setCode] = useState("");
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSendCode() {
    setSending(true);
    setError(null);
    try {
      const res = await fetch("/api/auth/deletion-code", { method: "POST" });
      if (res.ok) {
        setCodeSent(true);
      } else if (res.status === 409) {
        setError(t("deleteBlocked"));
      } else {
        const data = await res.json().catch(() => ({}));
        setError(data.error || tErrors("generic"));
      }
    } catch {
      setError(tErrors("generic"));
    } finally {
      setSending(false);
    }
  }

  async function handleDelete(e: React.FormEvent) {
    e.preventDefault();
    setDeleting(true);
    setError(null);
    try {
      const body: DeleteAccountRequest = { currentPassword, code: code.trim() };
      const res = await fetch("/api/auth/delete-account", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        setError(data.error || t("deleteFailed"));
        return;
      }
      // The session cookie is already gone; this forgets the user here too.
      await logout("/account-deleted");
    } catch {
      setError(t("deleteFailed"));
    } finally {
      setDeleting(false);
    }
  }

  // Closed, it's one quiet line under the other settings; open, it shows the warning and the form.
  return (
    <details>
      <summary className="cursor-pointer list-none px-1 text-sm text-muted transition-colors hover:text-red-accent [&::-webkit-details-marker]:hidden">
        {t("deleteTitle")}
      </summary>

      <div className="mt-3 rounded-2xl border border-red-accent/30 bg-card-bg p-6">
        <p className="text-sm">{t("deleteWarning")}</p>
        <ul className="mt-2 mb-3 list-disc space-y-1 pl-5 text-sm">
          <li>{t("deleteItemNotes")}</li>
          <li>{t("deleteItemSessions")}</li>
        </ul>
        <p className="mb-4 text-sm font-medium text-red-accent">{t("deleteCannotUndo")}</p>

        {codeSent && (
          <div role="status" className="mb-4 rounded-2xl border border-green-accent/30 bg-green-accent/10 px-4 py-3 text-sm text-green-accent">
            {t("codeSent")}
          </div>
        )}

        <button
          type="button"
          onClick={handleSendCode}
          disabled={sending}
          className="w-full rounded-2xl border border-card-border py-3 text-center text-sm font-semibold transition-colors hover:border-muted/40 disabled:opacity-50"
        >
          {sending ? t("sendingCode") : codeSent ? t("resendCode") : t("sendCode")}
        </button>

        {codeSent && (
          <form onSubmit={handleDelete} className="mt-4 space-y-4">
            <div>
              <label htmlFor="deletion-code" className="mb-1.5 block text-sm font-medium">
                {t("deletionCode")}
              </label>
              <input
                id="deletion-code"
                type="text"
                inputMode="numeric"
                autoComplete="one-time-code"
                pattern="[0-9]{6}"
                maxLength={6}
                value={code}
                onChange={(e) => setCode(e.target.value)}
                required
                className={inputClass}
              />
            </div>

            <div>
              <label htmlFor="delete-current-password" className="mb-1.5 block text-sm font-medium">
                {t("currentPassword")}
              </label>
              <input
                id="delete-current-password"
                type="password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                autoComplete="current-password"
                required
                className={inputClass}
              />
            </div>

            <button
              type="submit"
              disabled={deleting}
              className="w-full rounded-2xl bg-red-accent py-3 text-center text-sm font-semibold text-white transition-colors hover:bg-red-accent/90 disabled:opacity-50"
            >
              {deleting ? t("deletingAccount") : t("deleteAccount")}
            </button>
          </form>
        )}

        {error && (
          <div role="alert" className="mt-4 rounded-2xl border border-red-accent/30 bg-red-accent/10 px-4 py-3 text-sm text-red-accent">
            {error}
          </div>
        )}
      </div>
    </details>
  );
}
