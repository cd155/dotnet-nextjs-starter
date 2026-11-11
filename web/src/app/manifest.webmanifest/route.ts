import type { MetadataRoute } from "next";
import { getLocale } from "@/lib/i18n/server";
import { getMessages } from "@/lib/i18n/messages";
import { createTranslator } from "@/lib/i18n/translate";
import { isLocale } from "@/lib/i18n/config";

// The PWA install prompt and home-screen entry in the reader's language.
//
// A route handler rather than the `app/manifest.ts` convention because browsers
// fetch a manifest without credentials, so this request never sees the
// app_locale cookie. The root layout stamps the locale on the link instead; a
// bare request falls back to the cookie and Accept-Language.
export async function GET(request: Request) {
  const requested = new URL(request.url).searchParams.get("lang") ?? undefined;
  const locale = isLocale(requested) ? requested : await getLocale();
  const t = createTranslator(getMessages(locale), locale);

  const manifest: MetadataRoute.Manifest = {
    name: t("common.appName"),
    short_name: t("common.appName"),
    description: t("landing.metaDescription"),
    lang: locale,
    icons: [
      { src: "/icon-192.png", sizes: "192x192", type: "image/png" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png" },
    ],
    start_url: "/",
    display: "standalone",
    // The app icon's background.
    theme_color: "#111827",
    background_color: "#ffffff",
  };

  return Response.json(manifest, {
    headers: { "Content-Type": "application/manifest+json" },
  });
}
