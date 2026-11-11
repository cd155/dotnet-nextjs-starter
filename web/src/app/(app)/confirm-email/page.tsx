"use client";

import { Suspense, useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import type { ConfirmEmailRequest } from "@/lib/types";
import { AuthCard, authButtonClass } from "@/components/auth-card";
import { useTranslations } from "@/lib/i18n/client";

type Status = "confirming" | "confirmed" | "invalid" | "rateLimited" | "failed";

// The page the emailed link opens. It confirms on load and doesn't sign in: the link works until it expires, so
// signing in would make it a reusable login link.
function ConfirmEmail() {
  const t = useTranslations("emailConfirmation");
  const tErrors = useTranslations("errors");
  const searchParams = useSearchParams();
  const userId = searchParams.get("userId");
  const code = searchParams.get("code");
  const [status, setStatus] = useState<Status>(userId && code ? "confirming" : "invalid");

  useEffect(() => {
    if (!userId || !code) return;
    let cancelled = false;

    // Posting twice (React's development double effect) is fine: an already confirmed email answers 204.
    async function confirm(body: ConfirmEmailRequest) {
      try {
        const res = await fetch("/api/auth/confirm-email", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body),
        });
        if (!cancelled) {
          setStatus(
            res.ok ? "confirmed" : res.status === 400 ? "invalid" : res.status === 429 ? "rateLimited" : "failed"
          );
        }
      } catch {
        if (!cancelled) setStatus("failed");
      }
    }
    confirm({ userId, code });

    return () => {
      cancelled = true;
    };
  }, [userId, code]);

  switch (status) {
    case "confirming":
      return (
        <AuthCard title={t("confirming")}>
          <div className="mx-auto h-6 w-6 animate-spin rounded-full border-2 border-card-border border-t-primary" />
        </AuthCard>
      );
    case "confirmed":
      return (
        <AuthCard title={t("confirmedTitle")}>
          <p className="mb-4 text-sm leading-relaxed">{t("confirmed")}</p>
          <Link href="/login" className={`block ${authButtonClass}`}>
            {t("logIn")}
          </Link>
        </AuthCard>
      );
    case "invalid":
      return (
        <AuthCard title={t("invalidTitle")}>
          <p className="mb-4 text-sm leading-relaxed">{t("invalid")}</p>
          <Link href="/check-email" className={`block ${authButtonClass}`}>
            {t("sendNewLink")}
          </Link>
        </AuthCard>
      );
    case "rateLimited":
    case "failed":
      return (
        <AuthCard title={t("failedTitle")}>
          <p role="alert" className="text-sm leading-relaxed text-red-accent">
            {status === "rateLimited" ? tErrors("rateLimit") : tErrors("generic")}
          </p>
        </AuthCard>
      );
  }
}

export default function ConfirmEmailPage() {
  return (
    <Suspense>
      <ConfirmEmail />
    </Suspense>
  );
}
