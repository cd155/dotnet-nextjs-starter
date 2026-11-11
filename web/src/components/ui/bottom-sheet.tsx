"use client";

import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { cn } from "@/lib/utils";
import { useTranslations } from "@/lib/i18n/client";

interface BottomSheetProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  /**
   * Padding for the scrollable body. Override for content that needs to run full-bleed to the
   * sheet's edges, such as a list of rows that carry their own horizontal padding.
   */
  contentClassName?: string;
  children: React.ReactNode;
}

/**
 * The app's sheet: a scrim, a panel that slides up from the bottom over 300ms, a drag handle, a title
 * row with a close button, and a focus trap. On a wide screen the panel is a dialog in the middle that
 * fades in, without the handle. It stays mounted for the length of the close animation so the panel
 * can leave, then returns focus to whatever opened it.
 */
export function BottomSheet({
  isOpen,
  onClose,
  title,
  contentClassName = "px-5 pb-10",
  children,
}: BottomSheetProps) {
  const t = useTranslations();
  const [visible, setVisible] = useState(false);
  const [animateIn, setAnimateIn] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);
  const openerRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    let raf1 = -1;
    let raf2 = -1;
    let timer = -1;

    if (isOpen) {
      openerRef.current = document.activeElement as HTMLElement;
      // Two frames: the first mounts the panel off-screen, the second starts the transition.
      // Setting both in one frame would apply the final transform before the browser has
      // painted the initial one, and nothing would animate.
      raf1 = requestAnimationFrame(() => {
        setVisible(true);
        raf2 = requestAnimationFrame(() => {
          setAnimateIn(true);
          // Only claim focus if a child has not already taken it. A sheet that autofocuses a field
          // on mount would otherwise have focus pulled back to a non-interactive element a frame
          // later, making keyboard users tab before they can type.
          const panel = panelRef.current;
          if (panel && !panel.contains(document.activeElement)) {
            panel.focus();
          }
        });
      });
    } else if (visible) {
      raf1 = requestAnimationFrame(() => setAnimateIn(false));
      timer = window.setTimeout(() => {
        setVisible(false);
        openerRef.current?.focus();
        openerRef.current = null;
      }, 300);
    }

    return () => {
      cancelAnimationFrame(raf1);
      cancelAnimationFrame(raf2);
      clearTimeout(timer);
    };
  }, [isOpen]); // eslint-disable-line react-hooks/exhaustive-deps

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Escape") {
      onClose();
      return;
    }
    if (e.key !== "Tab") return;
    const panel = panelRef.current;
    if (!panel) return;
    const focusable = Array.from(
      panel.querySelectorAll<HTMLElement>(
        "button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex='-1'])"
      )
    );
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    // The panel itself holds focus right after opening, so Shift+Tab from it must wrap too.
    if (e.shiftKey && (document.activeElement === first || document.activeElement === panel)) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  if (!visible) return null;

  // On the body, so a sheet opened from the library's sticky column isn't caught in that column's layer,
  // where the page beside it would paint over its scrim.
  return createPortal(
    <>
      <div
        className="fixed inset-0 z-50 bg-black/50 transition-opacity duration-300"
        style={{ opacity: animateIn ? 1 : 0 }}
        onClick={onClose}
        aria-hidden="true"
      />

      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        onKeyDown={handleKeyDown}
        className={cn(
          "fixed inset-x-0 bottom-0 z-50 flex max-h-[85vh] flex-col rounded-t-2xl bg-card-bg transition-[translate,opacity] duration-300 outline-none",
          // On a wide screen, a dialog in the middle that fades in, rather than a sheet from the bottom edge.
          "lg:inset-x-auto lg:bottom-auto lg:top-1/2 lg:left-1/2 lg:max-h-[80vh] lg:w-full lg:max-w-md lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-2xl lg:shadow-xl",
          animateIn ? "translate-y-0 lg:opacity-100" : "translate-y-full lg:opacity-0",
        )}
      >
        {/* Drag handle */}
        <div className="flex justify-center pt-3 pb-1 lg:hidden">
          <div className="h-1 w-10 rounded-full bg-gray-300 dark:bg-gray-600" />
        </div>

        {/* Title row */}
        <div className="flex items-center justify-between gap-3 px-5 py-3">
          {/* A long title, such as a note's, wraps rather than squeezing the close button. */}
          <span className="min-w-0 text-base font-semibold break-words">{title}</span>
          <button
            onClick={onClose}
            aria-label={t("common.closeNamed", { name: title })}
            className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-card-border text-muted transition-opacity active:opacity-70"
          >
            ✕
          </button>
        </div>

        {/* Scrollable content */}
        <div className={cn("overflow-y-auto", contentClassName)}>{children}</div>
      </div>
    </>,
    document.body,
  );
}
