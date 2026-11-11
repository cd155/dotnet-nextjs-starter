"use client";

import { cn } from "@/lib/utils";
import { NotebookPen } from "lucide-react";
import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { AccountMenu } from "@/components/account-menu";
import { useAuth } from "@/contexts/auth-context";
import { useTranslations } from "@/lib/i18n/client";

// The signed-in places. Add a feature's page here.
const SECTIONS = [{ href: "/notes", label: "nav.notes", Icon: NotebookPen }] as const;

/**
 * The bar atop every page, signed in or not (it comes with `PageFrame`): the logo, the signed-in places, and the
 * account. On a phone the places are icons beside the name: their names stay for screen readers.
 */
export function AppNav() {
  const t = useTranslations();
  const pathname = usePathname();
  const { user } = useAuth();

  return (
    <nav aria-label={t("nav.primary")} className="border-b border-card-border bg-card-bg">
      {/* The page's gutter, so the logo lines up with what's below it. Wraps instead of overflowing
          when enlarged text leaves no room: the links drop below the logo, and each label stays whole
          on its line. */}
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 gap-y-2 px-6 py-4 sm:gap-x-8">
        {/* Signed in, "/" would only redirect to the notes. */}
        <Link href={user ? "/notes" : "/"} className="flex shrink-0 items-center gap-2 text-lg font-bold tracking-tight">
          {/* The ring keeps the icon's edge visible against a dark bar, as in the panel's header. */}
          <Image src="/icon-192.png" alt="" width={28} height={28} priority className="rounded-md dark:ring-1 dark:ring-white/25" />
          {/* Gone only where it would push the bar onto two rows, as on a 320px phone. */}
          <span className="max-[22.5rem]:sr-only">{t("common.appName")}</span>
        </Link>
        {user && (
        <ul className="flex flex-wrap items-center gap-x-5 gap-y-2 sm:gap-x-6">
          {SECTIONS.map(({ href, label, Icon }) => {
            const active = pathname === href || pathname.startsWith(`${href}/`);
            return (
              <li key={href}>
                <Link
                  href={href}
                  aria-current={active ? "page" : undefined}
                  className={cn(
                    "flex items-center gap-1.5 whitespace-nowrap text-sm font-medium transition-colors hover:text-foreground",
                    // The underline is a border, so it runs under the icon too, and under the icon alone on a phone.
                    "-mb-1.5 border-b-2 pb-1",
                    active ? "border-primary text-foreground" : "border-transparent text-foreground/70",
                  )}
                >
                  <Icon className="size-4.5 shrink-0" aria-hidden />
                  <span className="max-sm:sr-only">{t(label)}</span>
                </Link>
              </li>
            );
          })}
        </ul>
        )}
        <div className="ml-auto flex items-center gap-3 sm:gap-4">
          <AccountMenu />
        </div>
      </div>
    </nav>
  );
}
