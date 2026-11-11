"use client";

import { Suspense, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { Turnstile, type TurnstileHandle } from "@/components/turnstile";
import { useAuth } from "@/contexts/auth-context";
import { checkEmailPath, resendConfirmation } from "@/lib/email-confirmation";
import { EmailNotConfirmedError } from "@/lib/errors";
import { useTranslations } from "@/lib/i18n/client";

type Mode = "login" | "signup";

// The steps of signing up or in. Once signed in there is nothing to return to there: /check-email
// without its ?email (the header's Sign in link keeps only the path) would ask for an address again.
const ACCOUNT_PAGES = ["/login", "/check-email", "/confirm-email", "/forgot-password", "/reset-password"];

// Restrict to same-origin paths to prevent open-redirect attacks. Resolving against our own
// origin catches what a prefix check misses: "/\\evil.example" parses as a network-path URL.
// Dot segments can still normalise to a "//host" path ("/.//evil.example"), which router.push would
// treat as protocol-relative, so that is rejected too; a malformed value ("//[") falls back rather
// than throwing after sign-in has already succeeded.
// Browser-only (reads window), so it runs on submit rather than during render.
function safeRedirect(raw: string | null): string {
  if (raw?.startsWith("/")) {
    try {
      const url = new URL(raw, window.location.origin);
      if (
        url.origin === window.location.origin &&
        !url.pathname.startsWith("//") &&
        !ACCOUNT_PAGES.includes(url.pathname)
      ) {
        return url.pathname + url.search + url.hash;
      }
    } catch {
      // Malformed URL: use the default below.
    }
  }
  return "/notes";
}

function LoginForm({ siteKey }: { siteKey: string | null }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { login, register } = useAuth();
  const t = useTranslations("auth");
  const tConfirm = useTranslations("emailConfirmation");
  const tErrors = useTranslations("errors");

  // A "Create an account" link opens the sign-up form: /login?mode=register.
  const [mode, setMode] = useState<Mode>(() => (searchParams.get("mode") === "register" ? "signup" : "login"));
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  // Sign-in was refused because the email waits for confirmation, so the alert offers a new link.
  const [unconfirmed, setUnconfirmed] = useState(false);
  const [resending, setResending] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  // Signing up and resending the link email an address nobody has proved is theirs, so both need Turnstile's
  // token. Without a site key (local development) there's no check.
  const [turnstileToken, setTurnstileToken] = useState<string | null>(null);
  const turnstile = useRef<TurnstileHandle>(null);
  const showCheck = siteKey !== null && (mode === "signup" || unconfirmed);
  const waitingForCheck = siteKey !== null && turnstileToken === null;

  function switchMode(newMode: Mode) {
    setMode(newMode);
    setError(null);
    setUnconfirmed(false);
  }

  async function handleResend() {
    setResending(true);
    const result = await resendConfirmation(email, turnstileToken);
    setResending(false);
    if (result === "sent") {
      router.push(checkEmailPath(email));
    } else {
      // The Resend button stays, so the user can try again.
      turnstile.current?.reset();
      setError(
        result === "rateLimited"
          ? tErrors("rateLimit")
          : result === "checkFailed"
            ? tConfirm("checkFailed")
            : tConfirm("sendFailed")
      );
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setUnconfirmed(false);

    if (!email.trim()) {
      setError(t("emailRequired"));
      return;
    }

    if (password.length < 8) {
      setError(t("passwordTooShort"));
      return;
    }

    if (mode === "signup") {
      if (!displayName.trim()) {
        setError(t("displayNameRequired"));
        return;
      }
      if (password !== confirmPassword) {
        setError(t("passwordsDoNotMatch"));
        return;
      }
    }

    setSubmitting(true);

    try {
      if (mode === "login") {
        await login(email, password);
        router.push(safeRedirect(searchParams.get("redirect")));
        router.refresh();
      } else {
        await register(email, password, displayName.trim(), turnstileToken);
        router.push(checkEmailPath(email));
      }
    } catch (err) {
      // The token is spent either way.
      turnstile.current?.reset();
      setUnconfirmed(err instanceof EmailNotConfirmedError);
      setError(err instanceof Error ? err.message : t("genericError"));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="rounded-2xl border border-card-border bg-card-bg p-6">
      <h1 className="mb-6 text-center text-lg font-bold">
        {mode === "login" ? t("logInTitle") : t("signUpTitle")}
      </h1>

      <form onSubmit={handleSubmit} className="space-y-4">
        {mode === "signup" && (
          <div>
            <label htmlFor="display-name" className="mb-1.5 block text-sm font-medium">
              {t("displayName")}
            </label>
            <input
              id="display-name"
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder={t("displayNamePlaceholder")}
              autoComplete="name"
              className="w-full rounded-2xl border border-card-border bg-card-bg px-4 py-3 text-sm outline-none transition-colors focus:border-primary"
            />
          </div>
        )}

        <div>
          <label htmlFor="email" className="mb-1.5 block text-sm font-medium">{t("email")}</label>
          <input
            id="email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder={t("emailPlaceholder")}
            autoComplete="email"
            className="w-full rounded-2xl border border-card-border bg-card-bg px-4 py-3 text-sm outline-none transition-colors focus:border-primary"
          />
        </div>

        <div>
          <label htmlFor="password" className="mb-1.5 block text-sm font-medium">{t("password")}</label>
          <input
            id="password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder={t("passwordPlaceholder")}
            autoComplete={
              mode === "login" ? "current-password" : "new-password"
            }
            className="w-full rounded-2xl border border-card-border bg-card-bg px-4 py-3 text-sm outline-none transition-colors focus:border-primary"
          />
          {mode === "login" && (
            <div className="mt-1.5 text-right">
              <Link
                href={email.trim() ? `/forgot-password?email=${encodeURIComponent(email.trim())}` : "/forgot-password"}
                className="text-xs font-medium text-primary hover:underline"
              >
                {t("forgotPassword")}
              </Link>
            </div>
          )}
        </div>

        {mode === "signup" && (
          <div>
            <label htmlFor="confirm-password" className="mb-1.5 block text-sm font-medium">
              {t("confirmPassword")}
            </label>
            <input
              id="confirm-password"
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder={t("confirmPasswordPlaceholder")}
              autoComplete="new-password"
              className="w-full rounded-2xl border border-card-border bg-card-bg px-4 py-3 text-sm outline-none transition-colors focus:border-primary"
            />
          </div>
        )}

        {error && (
          <div role="alert" className="rounded-2xl border border-red-accent/30 bg-red-accent/10 px-4 py-3 text-sm text-red-accent">
            {error}
            {unconfirmed && (
              <button
                type="button"
                onClick={handleResend}
                disabled={resending || waitingForCheck}
                className="mt-2 block font-semibold underline disabled:opacity-50"
              >
                {resending ? tConfirm("sending") : tConfirm("resendConfirmation")}
              </button>
            )}
          </div>
        )}

        {showCheck && siteKey && <Turnstile siteKey={siteKey} onToken={setTurnstileToken} ref={turnstile} />}

        <button
          type="submit"
          disabled={submitting || (mode === "signup" && waitingForCheck)}
          className="w-full rounded-2xl bg-primary py-3 text-center text-sm font-semibold text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
        >
          {submitting
            ? mode === "login"
              ? t("loggingIn")
              : t("creatingAccount")
            : mode === "login"
              ? t("logInTitle")
              : t("signUpTitle")}
        </button>
      </form>

      <div className="mt-6 text-center text-sm text-muted">
        {mode === "login" ? (
          <>
            {t("noAccount")}{" "}
            <button
              onClick={() => switchMode("signup")}
              className="font-medium text-primary hover:underline"
            >
              {t("signUpTitle")}
            </button>
          </>
        ) : (
          <>
            {t("haveAccount")}{" "}
            <button
              onClick={() => switchMode("login")}
              className="font-medium text-primary hover:underline"
            >
              {t("logInTitle")}
            </button>
          </>
        )}
      </div>
    </div>
  );
}

export function LoginPage({ siteKey }: { siteKey: string | null }) {
  const t = useTranslations("common");

  return (
    <div className="flex min-h-[60vh] items-center justify-center">
      <div className="w-full max-w-sm">
        <div className="mb-6 text-center">
          <Link href="/" className="text-2xl font-bold">
            {t("appName")}
          </Link>
        </div>

        <Suspense>
          <LoginForm siteKey={siteKey} />
        </Suspense>
      </div>
    </div>
  );
}
