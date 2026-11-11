"use client";

import { useId } from "react";
import { cn } from "@/lib/utils";

type TextFieldSize = "md" | "lg";

const SIZES: Record<TextFieldSize, string> = {
  md: "text-sm",
  // A single prominent value, such as a quantity.
  lg: "text-lg font-semibold",
};

interface TextFieldProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "size"> {
  label: string;
  /** Secondary text shown beside the label in a lighter weight, e.g. "(optional)". */
  hint?: string;
  size?: TextFieldSize;
}

export function TextField({
  label,
  hint,
  size = "md",
  className,
  id,
  ...props
}: TextFieldProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;

  return (
    <div className="space-y-2">
      <label htmlFor={inputId} className="block text-sm font-semibold">
        {label}
        {hint && <span className="ml-1 font-normal text-muted">{hint}</span>}
      </label>
      <input
        id={inputId}
        type="text"
        className={cn(
          "w-full rounded-2xl border border-card-border bg-card-bg px-4 py-3 outline-none focus:border-primary disabled:opacity-50",
          SIZES[size],
          className
        )}
        {...props}
      />
    </div>
  );
}
