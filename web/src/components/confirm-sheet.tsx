"use client";

import { Button } from "@/components/ui/button";
import { useState } from "react";
import { BottomSheet } from "@/components/ui/bottom-sheet";

interface ConfirmSheetProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  body: string;
  confirmLabel: string;
  cancelLabel: string;
  /** Red, for something that can't be undone. */
  danger?: boolean;
  /** Its error, thrown, shows in the sheet. */
  onConfirm: () => Promise<void>;
}

/** A question in the app's bottom sheet, before something that changes more than the page shows. */
export function ConfirmSheet({
  isOpen,
  onClose,
  title,
  body,
  confirmLabel,
  cancelLabel,
  danger = false,
  onConfirm,
}: ConfirmSheetProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // An error belongs to one try: the sheet opens again without it.
  function close() {
    setError(null);
    onClose();
  }

  async function confirm() {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      await onConfirm();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <BottomSheet isOpen={isOpen} onClose={close} title={title}>
      <p className="text-sm text-muted">{body}</p>
      {error && (
        <p role="alert" className="mt-2 text-sm text-red-accent">
          {error}
        </p>
      )}
      <div className="mt-5 flex justify-end gap-2">
        <Button variant="outline" onClick={close}>
          {cancelLabel}
        </Button>
        <Button variant={danger ? "danger" : "primary"} aria-disabled={busy} onClick={() => void confirm()}>
          {confirmLabel}
        </Button>
      </div>
    </BottomSheet>
  );
}
