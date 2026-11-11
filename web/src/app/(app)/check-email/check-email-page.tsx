"use client";

import { Suspense, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { AuthCard, authButtonClass, authInputClass } from "@/components/auth-card";
import { Turnstile, type TurnstileHandle } from "@/components/turnstile";
import { checkEmailPath, resendConfirmation, type ResendResult } from "@/lib/email-confirmation";
import { useTranslations } from "@/lib/i18n/client";

// An email was just sent when this page opens, so Resend waits this long first, and again after each send.
const COOLDOWN_SECONDS = 60;

type Notice = { ok: boolean; text: string };

function useFailureText() {
  const t = useTranslations("emailConfirmation");
  const tErrors = useTranslations("errors");
  return (result: Exclude<ResendResult, "sent">) =>
    result === "rateLimited" ? tErrors("rateLimit") : result === "checkFailed" ? t("checkFailed") : t("sendFailed");
}

/**
 * Turnstile's token for resending, which emails an address nobody has proved is theirs.
 * Without a site key (local development) there's no check, and nothing waits for one.
 */
function useTurnstile(siteKey: string | null) {
  const [token, setToken] = useState<string | null>(null);
  const ref = useRef<TurnstileHandle>(null);
  const widget = siteKey ? <Turnstile siteKey={siteKey} onToken={setToken} ref={ref} /> : null;
  return { token, waiting: siteKey !== null && token === null, widget, reset: () => ref.current?.reset() };
}

function NoticeBox({ notice }: { notice: Notice }) {
  return notice.ok ? (
    <div role="status" className="rounded-2xl border border-green-accent/30 bg-green-accent/10 px-4 py-3 text-sm text-green-accent">
      {notice.text}
    </div>
  ) : (
    <div role="alert" className="rounded-2xl border border-red-accent/30 bg-red-accent/10 px-4 py-3 text-sm text-red-accent">
      {notice.text}
    </div>
  );
}

/** After sign-up, or after asking for a new link: where it went, and a Resend button with a cooldown. */
function SentTo({ email, siteKey }: { email: string; siteKey: string | null }) {
  const t = useTranslations("emailConfirmation");
  const failureText = useFailureText();
  const check = useTurnstile(siteKey);
  const [cooldown, setCooldown] = useState(COOLDOWN_SECONDS);
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);

  useEffect(() => {
    if (cooldown <= 0) return;
    const timer = setTimeout(() => setCooldown((seconds) => seconds - 1), 1000);
    return () => clearTimeout(timer);
  }, [cooldown]);

  async function handleResend() {
    setSending(true);
    setNotice(null);
    const result = await resendConfirmation(email, check.token);
    setSending(false);
    check.reset();
    if (result === "sent") {
      setNotice({ ok: true, text: t("resent") });
      setCooldown(COOLDOWN_SECONDS);
    } else {
      setNotice({ ok: false, text: failureText(result) });
    }
  }

  return (
    <AuthCard title={t("checkInboxTitle")}>
      <div className="space-y-4">
        <p className="text-sm leading-relaxed">{t("sentTo", { email })}</p>
        <p className="text-xs text-muted leading-relaxed">{t("spamHint")}</p>
        {notice && <NoticeBox notice={notice} />}
        {check.widget}
        <button type="button" onClick={handleResend} disabled={sending || cooldown > 0 || check.waiting} className={authButtonClass}>
          {sending ? t("sending") : cooldown > 0 ? t("resendIn", { seconds: cooldown }) : t("resend")}
        </button>
      </div>
    </AuthCard>
  );
}

/** Opened from an expired link, which doesn't carry the address: ask for it. */
function AskForLink({ siteKey }: { siteKey: string | null }) {
  const router = useRouter();
  const t = useTranslations("emailConfirmation");
  const tAuth = useTranslations("auth");
  const failureText = useFailureText();
  const check = useTurnstile(siteKey);
  const [email, setEmail] = useState("");
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setNotice(null);
    const address = email.trim();
    if (!address) {
      setNotice({ ok: false, text: tAuth("emailRequired") });
      return;
    }

    setSending(true);
    const result = await resendConfirmation(address, check.token);
    setSending(false);
    if (result === "sent") {
      router.replace(checkEmailPath(address));
    } else {
      check.reset();
      setNotice({ ok: false, text: failureText(result) });
    }
  }

  return (
    <AuthCard title={t("requestTitle")}>
      <form onSubmit={handleSubmit} className="space-y-4">
        <p className="text-sm leading-relaxed">{t("requestPrompt")}</p>
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
        {notice && <NoticeBox notice={notice} />}
        {check.widget}
        <button type="submit" disabled={sending || check.waiting} className={authButtonClass}>
          {sending ? t("sending") : t("sendLink")}
        </button>
      </form>
    </AuthCard>
  );
}

function CheckEmail({ siteKey }: { siteKey: string | null }) {
  const email = useSearchParams().get("email");
  // Keyed, so asking for a link for another address starts a fresh cooldown.
  return email ? <SentTo key={email} email={email} siteKey={siteKey} /> : <AskForLink siteKey={siteKey} />;
}

export function CheckEmailPage({ siteKey }: { siteKey: string | null }) {
  return (
    <Suspense>
      <CheckEmail siteKey={siteKey} />
    </Suspense>
  );
}
