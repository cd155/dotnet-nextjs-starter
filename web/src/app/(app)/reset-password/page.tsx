"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import type { ResetPasswordRequest } from "@/lib/types";
import { AuthCard, authButtonClass, authInputClass } from "@/components/auth-card";
import { useTranslations } from "@/lib/i18n/client";

type Status = "form" | "done" | "invalid";

// The page the emailed link opens. It doesn't sign in: the user logs in with the new password.
function ResetPassword() {
  const t = useTranslations("passwordReset");
  const tSettings = useTranslations("accountSettings");
  const tErrors = useTranslations("errors");
  const searchParams = useSearchParams();
  const userId = searchParams.get("userId");
  const code = searchParams.get("code");
  const [status, setStatus] = useState<Status>(userId && code ? "form" : "invalid");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!userId || !code) return;

    if (newPassword.length < 8) {
      setError(tSettings("passwordTooShort"));
      return;
    }
    if (newPassword !== confirmPassword) {
      setError(tSettings("passwordsDoNotMatch"));
      return;
    }

    setSubmitting(true);
    try {
      const body: ResetPasswordRequest = { userId, code, newPassword };
      const res = await fetch("/api/auth/reset-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (res.ok) {
        setStatus("done");
        return;
      }

      const data = await res.json().catch(() => ({}));
      if (data.invalidLink) {
        setStatus("invalid");
      } else {
        setError(data.error ?? tErrors("generic"));
      }
    } catch {
      setError(tErrors("generic"));
    } finally {
      setSubmitting(false);
    }
  }

  switch (status) {
    case "done":
      return (
        <AuthCard title={t("doneTitle")}>
          <p className="mb-4 text-sm leading-relaxed">{t("done")}</p>
          <Link href="/login" className={`block ${authButtonClass}`}>
            {t("logIn")}
          </Link>
        </AuthCard>
      );
    case "invalid":
      return (
        <AuthCard title={t("invalidTitle")}>
          <p className="mb-4 text-sm leading-relaxed">{t("invalid")}</p>
          <Link href="/forgot-password" className={`block ${authButtonClass}`}>
            {t("sendNewLink")}
          </Link>
        </AuthCard>
      );
    case "form":
      return (
        <AuthCard title={t("resetTitle")}>
          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label htmlFor="new-password" className="mb-1.5 block text-sm font-medium">
                {tSettings("newPassword")}
              </label>
              <input
                id="new-password"
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                placeholder={tSettings("newPasswordPlaceholder")}
                autoComplete="new-password"
                className={authInputClass}
              />
            </div>
            <div>
              <label htmlFor="confirm-password" className="mb-1.5 block text-sm font-medium">
                {tSettings("confirmNewPassword")}
              </label>
              <input
                id="confirm-password"
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                autoComplete="new-password"
                className={authInputClass}
              />
            </div>
            {error && (
              <div role="alert" className="rounded-2xl border border-red-accent/30 bg-red-accent/10 px-4 py-3 text-sm text-red-accent">
                {error}
              </div>
            )}
            <button type="submit" disabled={submitting} className={authButtonClass}>
              {submitting ? t("resetting") : t("resetPassword")}
            </button>
          </form>
        </AuthCard>
      );
  }
}

export default function ResetPasswordPage() {
  return (
    <Suspense>
      <ResetPassword />
    </Suspense>
  );
}
