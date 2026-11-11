import type { Metadata } from "next";
import { ContactLink } from "@/components/contact-link";
import { PageFrame } from "@/components/page-frame";
import { getTranslations } from "@/lib/i18n/server";

// The privacy policy. Public, signed in or not, so it's outside the proxy's matcher.

export async function generateMetadata(): Promise<Metadata> {
  const { t } = await getTranslations("privacy");
  return { title: t("metaTitle"), description: t("metaDescription") };
}

export default async function PrivacyPage() {
  const { t } = await getTranslations("privacy");

  return (
    <PageFrame className="mx-auto max-w-3xl px-6 py-14 sm:py-20">
      <h1 className="text-4xl font-bold tracking-tight">{t("title")}</h1>
      <p className="mt-3 text-sm text-muted">{t("updated")}</p>
      <p className="mt-8 text-lg leading-relaxed">{t("intro")}</p>

      <Section title={t("collectTitle")}>
        <p>{t("collectBody")}</p>
      </Section>

      <Section title={t("mcpTitle")}>
        <p>{t("mcpBody")}</p>
      </Section>

      <Section title={t("deleteTitle")}>
        <p>{t("deleteBody")}</p>
      </Section>

      <Section title={t("contactTitle")}>
        <p>
          {t("contactBody")} <ContactLink />.
        </p>
      </Section>
    </PageFrame>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mt-10">
      <h2 className="mb-3 text-xl font-semibold">{title}</h2>
      <div className="space-y-3 leading-relaxed text-muted">{children}</div>
    </section>
  );
}
