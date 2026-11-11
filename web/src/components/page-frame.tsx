import { AppNav } from "@/components/app-nav";
import { ErrorToastProvider } from "@/components/error-toast";
import { SiteFooter } from "@/components/site-footer";
import { cn } from "@/lib/utils";

/**
 * Every page's frame, signed in or not: the top bar, the page, and the footer, so no page has a bar of
 * its own. `className` sets the page's width and padding. The app's error toast covers the whole frame,
 * for the pages that report a failure through it.
 */
export function PageFrame({ className, children }: Readonly<{ className?: string; children: React.ReactNode }>) {
  return (
    <ErrorToastProvider>
      <div className="flex min-h-screen flex-col">
        <AppNav />
        <main className={cn("w-full flex-1", className)}>{children}</main>
        <SiteFooter />
      </div>
    </ErrorToastProvider>
  );
}
