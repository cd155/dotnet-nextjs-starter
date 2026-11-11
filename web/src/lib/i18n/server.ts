import { cookies, headers } from "next/headers";
import {
  DEFAULT_LOCALE,
  LOCALE_COOKIE,
  isLocale,
  negotiateLocale,
  type Locale,
} from "@/lib/i18n/config";
import { getMessages } from "@/lib/i18n/messages";
import {
  createTranslator,
  scopeTranslator,
  type Namespace,
  type ScopedTranslate,
  type Translate,
} from "@/lib/i18n/translate";

// Server-side locale access, for Server Components and Route Handlers.
// Mirrors the shape of getAuthCookie in @/lib/route-helpers.
//
// Note: reading a cookie opts the route into dynamic rendering. That is
// acceptable here because every backend fetch in @/lib/api.ts carries its own
// `next: { revalidate }` or `cache: "no-store"`, so response caching is
// unaffected by the change in render mode.
// An explicit choice always wins; the browser's Accept-Language only decides
// the first visit, before the reader has expressed one. English remains the
// last resort.
export async function getLocale(): Promise<Locale> {
  const store = await cookies();
  const chosen = store.get(LOCALE_COOKIE)?.value;
  if (isLocale(chosen)) return chosen;

  const requested = (await headers()).get("accept-language");
  return negotiateLocale(requested) ?? DEFAULT_LOCALE;
}

export async function getTranslations(): Promise<{
  t: Translate;
  locale: Locale;
}>;
export async function getTranslations<N extends Namespace>(
  namespace: N
): Promise<{ t: ScopedTranslate<N>; locale: Locale }>;
export async function getTranslations(namespace?: Namespace) {
  const locale = await getLocale();
  const t = createTranslator(getMessages(locale), locale);
  return { t: namespace === undefined ? t : scopeTranslator(t, namespace), locale };
}
