import type { Locale } from "@/lib/i18n/config";
import type { Messages } from "@/lib/i18n/translate";
import { en } from "@/lib/i18n/messages/en";

// One catalog per locale. To add a language, add its code to LOCALES (config.ts), copy messages/en to a folder of
// its own, annotate its object `Translation` (translate.ts) so a missing key is a compile error, and list it here.
export const MESSAGES: Record<Locale, Messages> = {
  en,
};

export function getMessages(locale: Locale): Messages {
  return MESSAGES[locale];
}
