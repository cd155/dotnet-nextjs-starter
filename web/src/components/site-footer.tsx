import Link from "next/link";
import { getTranslations } from "@/lib/i18n/server";

export async function SiteFooter() {
  const { t } = await getTranslations();

  return (
    <footer className="border-t border-card-border bg-card-bg py-6">
      {/* The top bar's width, so the copyright lines up with the logo. */}
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-6 gap-y-1.5 px-6">
        <p className="text-xs text-muted">{t("footer.copyright")}</p>
        <Link href="/privacy" className="text-xs text-muted hover:text-foreground">
          {t("footer.privacy")}
        </Link>
      </div>
    </footer>
  );
}
