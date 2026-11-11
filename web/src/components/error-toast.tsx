"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { useTranslations } from "@/lib/i18n/client";

interface ShowOptions {
  rateLimitMessage?: string;
  fallbackMessage?: string;
}

interface ErrorToastValue {
  /** Shows the error at the top of the screen for a few seconds: a rate limit as one, anything else as the fallback. */
  show: (error: unknown, options?: ShowOptions) => void;
}

const ErrorToastContext = createContext<ErrorToastValue | null>(null);

/**
 * The app's one place for an error that has no field or dialog of its own to show it in, such as a ☆
 * that didn't save. Mounted by `PageFrame`, around every page; components reach it with `useErrorToast()`.
 */
export function ErrorToastProvider({
  visibleMs = 3000,
  children,
}: {
  visibleMs?: number;
  children: React.ReactNode;
}) {
  const t = useTranslations();
  const [message, setMessage] = useState<string | null>(null);
  const timerRef = useRef<NodeJS.Timeout | null>(null);
  const pathname = usePathname();

  // Clear the timer on unmount
  useEffect(() => {
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  // Clear the message on route change. The provider sits in the
  // layout, which never unmounts between navigations, so an error from one
  // page would otherwise stay up over the next.
  useEffect(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    // Defer the state update to avoid synchronous setState inside an effect
    const t = setTimeout(() => setMessage(null), 0);
    return () => clearTimeout(t);
  }, [pathname]);

  const show = useCallback((error: unknown, options: ShowOptions = {}) => {
    // Check the numeric status first (set by ApiError, and by a failed push).
    // Fall back to message-string parsing for errors that crossed the
    // server→client boundary, where custom class properties are stripped.
    const err = error as { status?: number; message?: string } | null | undefined;
    const isRateLimit =
      err?.status === 429 ||
      err?.message?.includes("429") ||
      err?.message?.includes("rate limit");

    const text = isRateLimit
      ? (options.rateLimitMessage ?? t("errors.rateLimit"))
      : (options.fallbackMessage  ?? t("errors.generic"));

    // Show the toast
    if (timerRef.current) clearTimeout(timerRef.current);
    setMessage(text);
    timerRef.current = setTimeout(() => setMessage(null), visibleMs);
  }, [visibleMs, t]);

  const value = useMemo(() => ({ show }), [show]);

  return (
    <ErrorToastContext.Provider value={value}>
      {children}
      <ErrorToast message={message} />
    </ErrorToastContext.Provider>
  );
}

export function useErrorToast(): ErrorToastValue {
  const value = useContext(ErrorToastContext);
  if (!value) throw new Error("useErrorToast must be used within an ErrorToastProvider");
  return value;
}

function ErrorToast({ message }: { message: string | null }) {
  // The live region stays mounted, so screen readers announce each message as it arrives.
  return (
    <div role="alert" className="pointer-events-none fixed inset-x-0 top-4 z-50 flex justify-center px-4">
      {message && (
        <span className="rounded-full border border-red-accent/30 bg-card-bg px-4 py-1.5 text-sm text-red-accent shadow-lg">
          {message}
        </span>
      )}
    </div>
  );
}
