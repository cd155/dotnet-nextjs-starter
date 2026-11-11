"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { useTranslations } from "@/lib/i18n/client";

/** The centred card the sign-up follow-up pages (check your inbox, confirm email) sit in, as the login page does. */
export function AuthCard({ title, children }: { title: string; children: ReactNode }) {
  const t = useTranslations("common");

  return (
    <div className="flex min-h-[60vh] items-center justify-center">
      <div className="w-full max-w-sm">
        <div className="mb-6 text-center">
          <Link href="/" className="text-2xl font-bold">
            {t("appName")}
          </Link>
        </div>

        <div className="rounded-2xl border border-card-border bg-card-bg p-6">
          <h1 className="mb-4 text-center text-lg font-bold">{title}</h1>
          {children}
        </div>
      </div>
    </div>
  );
}

export const authButtonClass =
  "w-full rounded-2xl bg-primary py-3 text-center text-sm font-semibold text-white transition-colors hover:bg-primary/90 disabled:opacity-50";

export const authInputClass =
  "w-full rounded-2xl border border-card-border bg-card-bg px-4 py-3 text-sm outline-none transition-colors focus:border-primary";
