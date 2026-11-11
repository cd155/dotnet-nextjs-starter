"use client";

import { useEffect, useImperativeHandle, useRef, useState, type Ref } from "react";
import { useTranslations } from "@/lib/i18n/client";

// Cloudflare's script must be loaded from its own URL, never bundled or proxied.
const SCRIPT_URL = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";

type TurnstileApi = {
  render(
    element: HTMLElement,
    options: {
      sitekey: string;
      callback: (token: string) => void;
      "expired-callback": () => void;
      "error-callback": () => void;
      theme: "auto";
      size: "flexible";
    },
  ): string;
  reset(widgetId: string): void;
  remove(widgetId: string): void;
};

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

let loading: Promise<TurnstileApi> | null = null;

function loadTurnstile(): Promise<TurnstileApi> {
  loading ??= new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = SCRIPT_URL;
    script.async = true;
    script.onload = () => (window.turnstile ? resolve(window.turnstile) : reject(new Error("Turnstile didn't load")));
    script.onerror = () => {
      loading = null;
      script.remove();
      reject(new Error("Turnstile didn't load"));
    };
    document.head.appendChild(script);
  });
  return loading;
}

export type TurnstileHandle = {
  /** A token works once: call after each request that sent one, so the widget makes the next. */
  reset(): void;
};

/**
 * Cloudflare Turnstile's check that a person, not a script, fills in the form. It gives
 * `onToken` a token once it passes, and null when the token expires or the check fails. The API checks the token.
 * If Cloudflare's script can't load (blocked or offline), it says so, since the form can't be sent without it.
 */
export function Turnstile({
  siteKey,
  onToken,
  ref,
}: {
  siteKey: string;
  onToken: (token: string | null) => void;
  ref?: Ref<TurnstileHandle>;
}) {
  const container = useRef<HTMLDivElement>(null);
  const widget = useRef<string | null>(null);
  const [unavailable, setUnavailable] = useState(false);
  const t = useTranslations("emailConfirmation");
  // The latest callback, so a parent's re-render doesn't render the widget again.
  const onTokenRef = useRef(onToken);
  useEffect(() => {
    onTokenRef.current = onToken;
  });

  useImperativeHandle(ref, () => ({
    reset() {
      onTokenRef.current(null);
      if (widget.current) window.turnstile?.reset(widget.current);
    },
  }));

  useEffect(() => {
    let removed = false;
    loadTurnstile()
      .then((turnstile) => {
        if (removed || !container.current) return;
        widget.current = turnstile.render(container.current, {
          sitekey: siteKey,
          callback: (token) => onTokenRef.current(token),
          "expired-callback": () => onTokenRef.current(null),
          "error-callback": () => onTokenRef.current(null),
          theme: "auto",
          size: "flexible",
        });
      })
      .catch(() => {
        onTokenRef.current(null);
        if (!removed) setUnavailable(true);
      });

    return () => {
      removed = true;
      if (widget.current) window.turnstile?.remove(widget.current);
      widget.current = null;
      // Its token goes with it: the form must not send one the widget no longer shows.
      onTokenRef.current(null);
    };
  }, [siteKey]);

  if (unavailable) {
    return (
      <p role="alert" className="rounded-2xl border border-red-accent/30 bg-red-accent/10 px-4 py-3 text-sm text-red-accent">
        {t("checkUnavailable")}
      </p>
    );
  }

  return <div ref={container} className="min-h-[65px]" />;
}
