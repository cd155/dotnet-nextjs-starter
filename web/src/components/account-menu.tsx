"use client";

import { buttonClasses } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/**
 * The account's avatar: a letter on the page's colour. Sized in lines of its own text rather than a fixed size, so
 * the circle grows with enlarged text instead of the letter spilling out of it.
 */
const AVATAR_CLASSES =
  "flex size-[max(2rem,1.25lh)] shrink-0 items-center justify-center rounded-full bg-background text-sm font-semibold text-foreground";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/contexts/auth-context";
import { useTranslations } from "@/lib/i18n/client";
import { isProtectedPath } from "@/lib/protected-routes";

/**
 * The account, at the right of the top bar: signed in, the avatar, which opens the account page; signed out, Sign in,
 * which comes back to the page it was pressed on.
 */
export function AccountMenu() {
  const pathname = usePathname();
  const { user, loading } = useAuth();
  const t = useTranslations();

  // While the session is checked: on a signed-in page, a placeholder, so the bar doesn't shift; on a
  // public one, Sign in, so the server's page has it, since most of its visitors are signed out.
  if (loading && isProtectedPath(pathname)) return <div className={cn(AVATAR_CLASSES, "invisible")} />;

  if (!user) {
    // The login page is where it would lead.
    if (pathname === "/login") return null;
    return (
      <Link
        // Signed in, "/" would only redirect to the library, where the login page goes by default.
        href={pathname === "/" ? "/login" : `/login?redirect=${encodeURIComponent(pathname)}`}
        className={buttonClasses({ size: "sm", className: "whitespace-nowrap" })}
      >
        {t("nav.signIn")}
      </Link>
    );
  }

  const avatarLetter = (user.displayName || user.email)[0].toUpperCase();

  // The account page holds the password, Sign out everywhere and Log out, so the avatar goes straight there.
  return (
    <Link
      href="/account"
      aria-label={t("nav.account")}
      aria-current={pathname === "/account" ? "page" : undefined}
      className={AVATAR_CLASSES}
    >
      {avatarLetter}
    </Link>
  );
}
