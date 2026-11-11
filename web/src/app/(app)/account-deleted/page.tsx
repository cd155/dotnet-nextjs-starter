"use client";

import Link from "next/link";
import { AuthCard, authButtonClass } from "@/components/auth-card";
import { useTranslations } from "@/lib/i18n/client";

// Where the account page goes once the account is deleted.
export default function AccountDeletedPage() {
  const t = useTranslations("accountSettings");

  return (
    <AuthCard title={t("accountDeletedTitle")}>
      <div className="space-y-4">
        <p className="text-sm leading-relaxed">{t("accountDeletedBody")}</p>
        <Link href="/" className={`block ${authButtonClass}`}>
          {t("backHome")}
        </Link>
      </div>
    </AuthCard>
  );
}
