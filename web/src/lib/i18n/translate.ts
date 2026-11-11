import type { en } from "@/lib/i18n/messages/en";
import { DEFAULT_LOCALE, type Locale } from "@/lib/i18n/config";

// CLDR plural categories. `other` is the only one every language has, so a
// plural message declares it and adds whichever others its language needs:
// Chinese has only `other`, English adds `one`, Russian would add `few` and
// `many`. Which one a count selects is decided by Intl.PluralRules, not by us.
type PluralCategory = "zero" | "one" | "two" | "few" | "many" | "other";

export type PluralMessage = { other: string } & Partial<
  Record<Exclude<PluralCategory, "other">, string>
>;

// A node is a plural message if it carries an `other` string — so no catalog
// section may use "other" as an ordinary key.
type IsPlural<T> = T extends { other: string } ? true : false;

// `en` is declared `as const`, which types each leaf as its own literal. Widen
// the leaves back to `string` so other catalogs can hold different text, while
// keeping the key structure exact. A plural group widens to PluralMessage, so a
// translator only has to supply the categories their language actually uses.
type WidenLeaves<T> = {
  [K in keyof T]: T[K] extends string
    ? string
    : IsPlural<T[K]> extends true
      ? PluralMessage
      : WidenLeaves<T[K]>;
};

// The English catalog is the source of truth for the shape of every other
// catalog: another locale's is annotated `Translation`, so a missing or misspelt key is
// a compile error rather than a string that silently falls back at runtime.
// `Messages` is the widened form the app passes around once a catalog is built.
export type Messages = WidenLeaves<typeof en>;

// One locale file's slice of the catalog, so a namespace file can be typed
// against exactly the sections it owns.
export type MessagesFor<K extends keyof Messages> = Pick<Messages, K>;

type UnionToIntersection<U> = (U extends unknown ? (x: U) => void : never) extends (
  x: infer I
) => void
  ? I
  : never;

// A translated string has to carry every placeholder its English original has.
// Expressed as an intersection of patterns, so "{name} 有 {count} 个标签"
// satisfies `${string}{name}${string}` and `${string}{count}${string}` while a
// translation that renamed {name} to {title}, or dropped {count}, does not.
//
// This checks presence, not absence: a translation that *adds* {foo} still
// compiles and renders the token literally. Presence is the case that loses
// information silently, which is why it is the one worth a type for.
type TranslatedString<EN extends string> = [Placeholders<EN>] extends [never]
  ? string
  : string &
      UnionToIntersection<
        Placeholders<EN> extends infer P extends string ? `${string}{${P}}${string}` : never
      >;

// The shape a non-English catalog must satisfy: same keys as `en`, and each
// leaf carrying the same placeholders as the English message it translates.
export type Translation<T = typeof en> = {
  [K in keyof T]: T[K] extends string
    ? TranslatedString<T[K]>
    : T[K] extends { other: infer O extends string }
      ? { other: TranslatedString<O> } & Partial<
          Record<Exclude<PluralCategory, "other">, TranslatedString<O>>
        >
      : Translation<T[K]>;
};

// The same slice, for a catalog being translated: `MessagesFor` checks the key
// structure, `TranslationFor` also checks each message's placeholders.
export type TranslationFor<K extends keyof typeof en> = Translation<Pick<typeof en, K>>;

// Every leaf path in the catalog, as a dot-separated string — e.g. "nav.logOut".
// A plural group is itself a leaf: you address "notes.count" and pass a
// count, never "notes.count.one".
type LeafPaths<T> = {
  [K in keyof T & string]: T[K] extends string
    ? K
    : IsPlural<T[K]> extends true
      ? K
      : `${K}.${LeafPaths<T[K]>}`;
}[keyof T & string];

export type MessageKey = LeafPaths<Messages>;

export type TranslateVars = Record<string, string | number>;

// The placeholder names inside a message, read off the literal type — "{count}
// shares of {symbol}" gives "count" | "symbol". `en` is `as const`, so every
// message is its own literal and this is exact.
type Placeholders<S extends string> = S extends `${string}{${infer Name}}${infer Rest}`
  ? Name | Placeholders<Rest>
  : never;

// Walks a dot path into the catalog: MessageAt<typeof en, "nav.logOut">.
type MessageAt<T, P extends string> = P extends `${infer Head}.${infer Rest}`
  ? Head extends keyof T
    ? MessageAt<T[Head], Rest>
    : never
  : P extends keyof T
    ? T[P]
    : never;

// A plural group always takes `count` — that is what selects the form, even in
// a language whose text does not repeat the number.
type PlaceholdersOf<T> = T extends string
  ? Placeholders<T>
  : T extends object
    ? "count" | { [K in keyof T]: T[K] extends string ? Placeholders<T[K]> : never }[keyof T]
    : never;

type VarsFor<K extends MessageKey> = PlaceholdersOf<MessageAt<typeof en, K>>;

// Requiring exactly the message's own placeholders turns a renamed or forgotten
// variable — which used to render as a stray "{count}", or silently drop a word
// — into a compile error, the same way a misspelt key already is.
export type Translate = <K extends MessageKey>(
  key: K,
  ...args: [VarsFor<K>] extends [never]
    ? []
    : [vars: Record<VarsFor<K>, string | number>]
) => string;

// Messages that take no variables. A caller that cannot interpolate — an API
// error body, say — asks for one of these, so passing it a message with a
// placeholder is a compile error rather than a stray "{symbol}" on screen.
export type StaticMessageKey = {
  [K in MessageKey]: [VarsFor<K>] extends [never] ? K : never;
}[MessageKey];

// A namespace is a top-level section of the catalog, owned by one feature.
export type Namespace = keyof Messages & string;

// The keys inside one namespace, addressed relatively: KeysIn<"notes">
// includes "title" and "empty", not "notes.title".
type KeysIn<N extends Namespace> = MessageKey extends infer K
  ? K extends `${N}.${infer Rest}`
    ? Rest
    : never
  : never;

// `t` bound to a namespace. Call sites read t("pageTitle") instead of
// t("notes.title"), so moving a key between namespaces stops being a
// find-and-replace across the app — and a file that needs a second translator
// is visibly reaching into another feature's strings.
export type ScopedTranslate<N extends Namespace> = <K extends KeysIn<N>>(
  key: K,
  ...args: [VarsFor<`${N}.${K}` & MessageKey>] extends [never]
    ? []
    : [vars: Record<VarsFor<`${N}.${K}` & MessageKey>, string | number>]
) => string;

export function scopeTranslator<N extends Namespace>(
  t: Translate,
  namespace: N
): ScopedTranslate<N> {
  return ((key: string, vars?: TranslateVars) =>
    (t as (k: MessageKey, v?: TranslateVars) => string)(
      `${namespace}.${key}` as MessageKey,
      vars
    )) as ScopedTranslate<N>;
}

type MessageNode = string | PluralMessage | { [key: string]: MessageNode };

function lookup(node: MessageNode, path: string): MessageNode | undefined {
  let current: MessageNode | undefined = node;
  for (const segment of path.split(".")) {
    if (typeof current !== "object" || current === null) return undefined;
    current = (current as { [key: string]: MessageNode })[segment];
  }
  return current;
}

function isPluralMessage(node: MessageNode): node is PluralMessage {
  return typeof node === "object" && typeof node.other === "string";
}

// Replaces {name} placeholders. An unmatched placeholder is left as-is so it
// shows up in review rather than rendering as "undefined".
function interpolate(template: string, vars?: TranslateVars): string {
  if (!vars) return template;
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    name in vars ? String(vars[name]) : match
  );
}

// Constructing Intl.PluralRules is expensive relative to a lookup, and `t` runs
// once per rendered string, so keep one instance per locale.
const PLURAL_RULES = new Map<Locale, Intl.PluralRules>();

function pluralRules(locale: Locale): Intl.PluralRules {
  let rules = PLURAL_RULES.get(locale);
  if (!rules) {
    rules = new Intl.PluralRules(locale);
    PLURAL_RULES.set(locale, rules);
  }
  return rules;
}

// Picks the form for `vars.count`. Falls back to `other` both when the catalog
// has no entry for the selected category and when the caller passed no count —
// `other` is the form every language defines.
//
// One count per message: a message that pluralises two numbers at once has to
// be composed from two counted messages.
function resolvePlural(
  node: PluralMessage,
  locale: Locale,
  vars?: TranslateVars
): string {
  const count = vars?.count;
  if (typeof count !== "number") return node.other;
  const category = pluralRules(locale).select(count) as PluralCategory;
  return node[category] ?? node.other;
}

// No English fallback at runtime: every catalog is annotated `Messages`, so a
// missing key cannot compile, and importing `en` here to cover a case the type
// system already rules out would ship the whole English catalog to every
// reader. A key that somehow misses renders as itself — loudly — and warns in
// development. (Restore the fallback if catalogs ever load dynamically, where
// the compiler can no longer vouch for them.)
export function createTranslator(
  messages: Messages,
  locale: Locale = DEFAULT_LOCALE
): Translate {
  return ((key: MessageKey, vars?: TranslateVars) => {
    const node = lookup(messages as MessageNode, key);

    if (typeof node === "string") return interpolate(node, vars);
    if (node !== undefined && isPluralMessage(node)) {
      return interpolate(resolvePlural(node, locale, vars), vars);
    }
    if (process.env.NODE_ENV !== "production") {
      console.warn(`[i18n] missing message "${key}" for locale "${locale}"`);
    }
    return key;
  }) as Translate;
}
