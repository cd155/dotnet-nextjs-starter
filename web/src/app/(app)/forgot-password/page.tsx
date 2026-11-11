"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import type { ForgotPasswordRequest } from "@/lib/types";
import { AuthCard, authButtonClass, authInputClass } from "@/components/auth-card";
import { useTranslations } from "@/lib/i18n/client";

// The API answers the same whether or not the address has an account, so "sent" only means the request went through.
function ForgotPassword() {
  const t = useTranslations("passwordReset");
  const tAuth = useTranslations("auth");
  const tConfirm = useTranslations("emailConfirmation");
  const tErrors = useTranslations("errors");
  const searchParams = useSearchParams();
  // The login page passes on the address the user typed there.
  const [email, setEmail] = useState(searchParams.get("email") ?? "");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sentTo, setSentTo] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const address = email.trim();
    if (!address) {
      setError(tAuth("emailRequired"));
      return;
    }

    setSending(true);
    try {
      const body: ForgotPasswordRequest = { email: address };
      const res = await fetch("/api/auth/forgot-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (res.ok) {
        setSentTo(address);
      } else {
        setError(res.status === 429 ? tErrors("rateLimit") : tConfirm("sendFailed"));
      }
    } catch {
      setError(tConfirm("sendFailed"));
    } finally {
      setSending(false);
    }
  }

  if (sentTo) {
    return (
      <AuthCard title={t("sentTitle")}>
        <div className="space-y-4">
          <p className="text-sm leading-relaxed">{t("sentTo", { email: sentTo })}</p>
          <p className="text-xs text-muted leading-relaxed">{tConfirm("spamHint")}</p>
          <Link href="/login" className={`block ${authButtonClass}`}>
            {t("backToLogIn")}
          </Link>
        </div>
      </AuthCard>
    );
  }

  return (
    <AuthCard title={t("forgotTitle")}>
      <form onSubmit={handleSubmit} className="space-y-4">
        <p className="text-sm leading-relaxed">{t("forgotPrompt")}</p>
        <div>
          <label htmlFor="email" className="mb-1.5 block text-sm font-medium">{tAuth("email")}</label>
          <input
            id="email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder={tAuth("emailPlaceholder")}
            autoComplete="email"
            className={authInputClass}
          />
        </div>
        {error && (
          <div role="alert" className="rounded-2xl border border-red-accent/30 bg-red-accent/10 px-4 py-3 text-sm text-red-accent">
            {error}
          </div>
        )}
        <button type="submit" disabled={sending} className={authButtonClass}>
          {sending ? t("sending") : t("sendLink")}
        </button>
        <p className="text-center text-sm">
          <Link href="/login" className="font-medium text-primary hover:underline">
            {t("backToLogIn")}
          </Link>
        </p>
      </form>
    </AuthCard>
  );
}

export default function ForgotPasswordPage() {
  return (
    <Suspense>
      <ForgotPassword />
    </Suspense>
  );
}
