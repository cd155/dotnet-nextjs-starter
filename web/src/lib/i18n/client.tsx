"use client";

import { createContext, useContext, useMemo, type ReactNode } from "react";
import { DEFAULT_LOCALE, type Locale } from "@/lib/i18n/config";
import {
  createTranslator,
  scopeTranslator,
  type Messages,
  type Namespace,
  type ScopedTranslate,
  type Translate,
} from "@/lib/i18n/translate";

interface LocaleContextValue {
  locale: Locale;
  t: Translate;
}

// The default context holds no messages on purpose. The provider wraps the
// whole app from the root layout, so a component reading this one is rendered
// outside it by mistake — and rendering raw keys says so, where quietly
// falling back to English would hide the mistake from exactly the readers it
// affects.
const LocaleContext = createContext<LocaleContextValue>({
  locale: DEFAULT_LOCALE,
  t: createTranslator({} as Messages, DEFAULT_LOCALE),
});

// `messages` is passed down from the root layout, which already resolved the
// locale server-side. Only the active catalog crosses the boundary, and the
// first server render is already in the right language — so there is no
// hydration mismatch and no need for the mounted-flag dance used elsewhere for
// localStorage-backed preferences.
export function LocaleProvider({
  locale,
  messages,
  children,
}: {
  locale: Locale;
  messages: Messages;
  children: ReactNode;
}) {
  const value = useMemo(
    () => ({ locale, t: createTranslator(messages, locale) }),
    [locale, messages]
  );

  return (
    <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>
  );
}

// Pass a namespace to bind `t` to it — useTranslations("notes") gives
// t("pageTitle"). Without one you get the whole catalog and full key paths,
// which is what a component spanning several features needs.
export function useTranslations(): Translate;
export function useTranslations<N extends Namespace>(
  namespace: N
): ScopedTranslate<N>;
export function useTranslations(namespace?: Namespace) {
  const { t } = useContext(LocaleContext);
  return namespace === undefined ? t : scopeTranslator(t, namespace);
}

export function useLocale(): Locale {
  return useContext(LocaleContext).locale;
}
