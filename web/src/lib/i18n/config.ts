// Locale configuration shared by server and client. Keep this module free of
// `next/headers` and React imports so both sides can pull from it.

export const LOCALES = ["en"] as const;

export type Locale = (typeof LOCALES)[number];

export const DEFAULT_LOCALE: Locale = "en";

// Read server-side on every request to pick the catalog. A language menu, once there is more than one locale,
// writes it from a server action; the client receives its messages as props and never reads it.
export const LOCALE_COOKIE = "app_locale";

export function isLocale(value: string | undefined): value is Locale {
  return value !== undefined && (LOCALES as readonly string[]).includes(value);
}

// Picks the best supported locale from an Accept-Language header, so a reader
// whose browser asks for Chinese is not shown English until they find the
// language menu. Quality values are honoured, and a tag is matched on its
// primary subtag too ("en-GB" resolves to en) — the
// alternative is a first visit in the wrong language for anyone whose browser
// does not send our exact tag.
export function negotiateLocale(header: string | null): Locale | undefined {
  if (!header) return undefined;

  const ranked = header
    .split(",")
    .map((part) => {
      const [tag, ...params] = part.trim().split(";");
      const q = params
        .map((p) => p.trim())
        .find((p) => p.startsWith("q="))
        ?.slice(2);
      return { tag: tag.trim().toLowerCase(), q: q === undefined ? 1 : Number(q) };
    })
    .filter((entry) => entry.tag !== "" && !Number.isNaN(entry.q) && entry.q > 0)
    .sort((a, b) => b.q - a.q);

  for (const { tag } of ranked) {
    const exact = LOCALES.find((locale) => locale.toLowerCase() === tag);
    if (exact) return exact;

    const primary = tag.split("-")[0];
    const byPrimary = LOCALES.find(
      (locale) => locale.toLowerCase().split("-")[0] === primary
    );
    if (byPrimary) return byPrimary;
  }

  return undefined;
}
