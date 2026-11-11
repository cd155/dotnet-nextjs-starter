"use client";

import { useState } from "react";
import { buttonClasses } from "@/components/ui/button";
import { ChangePasswordSection } from "@/components/change-password-section";
import { ConfirmSheet } from "@/components/confirm-sheet";
import { DeleteAccountSection } from "@/components/delete-account-section";
import { useAuth } from "@/contexts/auth-context";
import { useTranslations } from "@/lib/i18n/client";

/** A titled group of settings rows in one card, the rows split by dividers. */
function SettingsGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2">
      <h2 className="px-1 text-sm font-bold text-muted/80">{title}</h2>
      <div className="divide-y divide-card-border rounded-2xl border border-card-border bg-card-bg">{children}</div>
    </section>
  );
}

export default function AccountPage() {
  const t = useTranslations("accountSettings");
  const tAll = useTranslations();
  const { user, logout, signOutEverywhere } = useAuth();
  const [signOutAllOpen, setSignOutAllOpen] = useState(false);

  const displayLabel = user ? user.displayName || user.email : null;

  return (
    <div className="mx-auto w-full max-w-md space-y-6">
      <h1 className="text-2xl font-bold">{t("title")}</h1>

      {user && (
        <div className="flex items-center gap-4 px-1">
          <div
            aria-hidden="true"
            className="flex size-12 shrink-0 items-center justify-center rounded-full bg-card-border text-lg font-semibold"
          >
            {displayLabel?.[0]?.toUpperCase()}
          </div>
          <div className="min-w-0">
            <p className="truncate font-semibold">{displayLabel}</p>
            {user.displayName && <p className="truncate text-sm text-muted">{user.email}</p>}
          </div>
        </div>
      )}

      <SettingsGroup title={t("securityTitle")}>
        <ChangePasswordSection />
        <div className="flex items-center justify-between gap-3 p-4">
          <p className="text-sm font-medium">{tAll("nav.signOutEverywhere")}</p>
          <button
            type="button"
            onClick={() => setSignOutAllOpen(true)}
            className={buttonClasses({ variant: "outline", size: "md", className: "shrink-0" })}
          >
            {t("signOutAction")}
          </button>
        </div>
      </SettingsGroup>

      <button
        type="button"
        onClick={() => logout()}
        className="w-full rounded-2xl border border-card-border bg-card-bg p-4 text-center text-sm font-semibold transition-colors hover:bg-card-border/30"
      >
        {tAll("nav.logOut")}
      </button>

      <DeleteAccountSection />

      <ConfirmSheet
        isOpen={signOutAllOpen}
        onClose={() => setSignOutAllOpen(false)}
        title={tAll("nav.signOutEverywhereTitle")}
        body={tAll("nav.signOutEverywhereBody")}
        confirmLabel={tAll("nav.signOutEverywhereConfirm")}
        cancelLabel={tAll("common.cancel")}
        danger
        onConfirm={async () => {
          await signOutEverywhere();
          setSignOutAllOpen(false);
        }}
      />
    </div>
  );
}
