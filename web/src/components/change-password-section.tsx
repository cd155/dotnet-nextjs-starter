"use client";

import { useState } from "react";
import type { ChangePasswordRequest } from "@/lib/types";
import { buttonClasses } from "@/components/ui/button";
import { useTranslations } from "@/lib/i18n/client";

const inputClass =
  "w-full rounded-2xl border border-card-border bg-card-bg px-4 py-3 text-sm outline-none transition-colors focus:border-primary";

/**
 * The Password row of the account page: closed, it shows a masked password and a Change button; open, the form to
 * change it.
 */
export function ChangePasswordSection() {
  const t = useTranslations("accountSettings");
  const tCommon = useTranslations("common");

  const [open, setOpen] = useState(false);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [changed, setChanged] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  function reset() {
    setCurrentPassword("");
    setNewPassword("");
    setConfirmPassword("");
    setError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (newPassword.length < 8) {
      setError(t("passwordTooShort"));
      return;
    }
    if (newPassword !== confirmPassword) {
      setError(t("passwordsDoNotMatch"));
      return;
    }

    setSubmitting(true);
    try {
      const body: ChangePasswordRequest = { currentPassword, newPassword };
      const res = await fetch("/api/auth/change-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        setError(data.error || t("changeFailed"));
        return;
      }
      reset();
      setOpen(false);
      setChanged(true);
    } catch {
      setError(t("changeFailed"));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="p-4">
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-medium">{t("passwordLabel")}</p>
          <p className="text-sm text-muted" aria-hidden="true">
            ••••••••
          </p>
        </div>
        {!open && (
          <button
            type="button"
            onClick={() => {
              setChanged(false);
              setOpen(true);
            }}
            className={buttonClasses({ variant: "outline", size: "md", className: "shrink-0" })}
          >
            {t("changeAction")}
          </button>
        )}
      </div>

      {changed && (
        <div role="status" className="mt-3 rounded-2xl border border-green-accent/30 bg-green-accent/10 px-4 py-3 text-sm text-green-accent">
          {t("passwordChanged")}
        </div>
      )}

      {open && (
        <form onSubmit={handleSubmit} className="mt-4 space-y-4">
          <div>
            <label htmlFor="current-password" className="mb-1.5 block text-sm font-medium">
              {t("currentPassword")}
            </label>
            <input
              id="current-password"
              type="password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              autoComplete="current-password"
              required
              autoFocus
              className={inputClass}
            />
          </div>

          <div>
            <label htmlFor="new-password" className="mb-1.5 block text-sm font-medium">
              {t("newPassword")}
            </label>
            <input
              id="new-password"
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder={t("newPasswordPlaceholder")}
              autoComplete="new-password"
              required
              className={inputClass}
            />
          </div>

          <div>
            <label htmlFor="confirm-new-password" className="mb-1.5 block text-sm font-medium">
              {t("confirmNewPassword")}
            </label>
            <input
              id="confirm-new-password"
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              autoComplete="new-password"
              required
              className={inputClass}
            />
          </div>

          {error && (
            <div role="alert" className="rounded-2xl border border-red-accent/30 bg-red-accent/10 px-4 py-3 text-sm text-red-accent">
              {error}
            </div>
          )}

          <div className="flex gap-3">
            <button
              type="button"
              onClick={() => {
                reset();
                setOpen(false);
              }}
              className="flex-1 rounded-2xl border border-card-border py-3 text-center text-sm font-semibold transition-colors hover:bg-card-border/40"
            >
              {tCommon("cancel")}
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="flex-1 rounded-2xl bg-primary py-3 text-center text-sm font-semibold text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
            >
              {submitting ? t("changingPassword") : t("changePassword")}
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
