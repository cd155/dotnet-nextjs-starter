import type { Metadata } from "next";
import { Bot, Database, Globe, KeyRound } from "lucide-react";
import { PageFrame } from "@/components/page-frame";
import { ButtonLink } from "@/components/ui/button";
import { getTranslations } from "@/lib/i18n/server";

// The home page, for signed-out visitors: the proxy sends a signed-in user on to /notes. Replace it with your app's.

const SIGN_UP_HREF = "/login?mode=register";

const FEATURES = [
  { icon: Database, title: "apiTitle", body: "apiBody" },
  { icon: KeyRound, title: "authTitle", body: "authBody" },
  { icon: Bot, title: "mcpTitle", body: "mcpBody" },
  { icon: Globe, title: "webTitle", body: "webBody" },
] as const;

export async function generateMetadata(): Promise<Metadata> {
  const { t } = await getTranslations("landing");
  return { title: t("metaTitle"), description: t("metaDescription") };
}

export default async function LandingPage() {
  const { t } = await getTranslations("landing");

  return (
    <PageFrame className="mx-auto max-w-5xl px-6 py-14 sm:py-20">
      <section className="mx-auto max-w-2xl text-center">
        <h1 className="text-4xl font-bold tracking-tight sm:text-5xl">{t("heroTitle")}</h1>
        <p className="mt-6 text-lg leading-relaxed text-muted">{t("heroBody")}</p>
        <div className="mt-8 flex flex-wrap justify-center gap-3">
          <ButtonLink href={SIGN_UP_HREF} size="lg" className="px-6">
            {t("createAccount")}
          </ButtonLink>
          <ButtonLink href="/notes" variant="outline" size="lg" className="border-muted/30 px-6">
            {t("openNotes")}
          </ButtonLink>
        </div>
      </section>

      <section className="mt-20">
        <h2 className="text-center text-2xl font-bold">{t("featuresTitle")}</h2>
        <ul className="mt-8 grid gap-4 sm:grid-cols-2">
          {FEATURES.map(({ icon: Icon, title, body }) => (
            <li key={title} className="rounded-2xl border border-card-border bg-card-bg p-6">
              <Icon className="size-6 text-primary" aria-hidden />
              <h3 className="mt-3 font-semibold">{t(title)}</h3>
              <p className="mt-2 text-sm leading-relaxed text-muted">{t(body)}</p>
            </li>
          ))}
        </ul>
      </section>
    </PageFrame>
  );
}
