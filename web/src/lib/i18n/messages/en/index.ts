// The English catalog, assembled from one file per namespace. This object's
// shape is the contract every other locale must satisfy (see `Messages` in
// ../../translate.ts), so a new string is added here first.
//
// A namespace is owned by one feature. A string used by more than one feature
// belongs in `common` — reaching into another feature's namespace couples the
// two together invisibly.
import { common } from "@/lib/i18n/messages/en/common";
import { errors } from "@/lib/i18n/messages/en/errors";
import { auth } from "@/lib/i18n/messages/en/auth";
import { marketing } from "@/lib/i18n/messages/en/marketing";
import { notes } from "@/lib/i18n/messages/en/notes";
import { legal } from "@/lib/i18n/messages/en/legal";

export const en = {
  ...common,
  ...errors,
  ...auth,
  ...marketing,
  ...notes,
  ...legal,
} as const;
