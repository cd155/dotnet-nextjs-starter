import Link from "next/link";
import { cn } from "@/lib/utils";

type ButtonVariant = "primary" | "outline" | "danger";
type ButtonSize = "sm" | "md" | "lg";
type ButtonShape = "pill" | "block";

const VARIANTS: Record<ButtonVariant, string> = {
  primary: "bg-primary text-white hover:bg-primary/90",
  outline: "border border-card-border bg-card-bg text-foreground hover:border-muted/40",
  danger: "bg-red-accent text-white hover:bg-red-accent/90",
};

const SIZES: Record<ButtonSize, string> = {
  sm: "px-3 py-1 text-xs font-semibold",
  md: "px-4 py-2 text-sm font-semibold",
  lg: "px-4 py-3 text-base font-semibold",
};

const SHAPES: Record<ButtonShape, string> = {
  pill: "rounded-full",
  // A block button always fills its container and centres its label, so the size's
  // horizontal padding is irrelevant — only the vertical rhythm carries over.
  block: "block w-full rounded-2xl text-center",
};

function classesFor(variant: ButtonVariant, size: ButtonSize, shape: ButtonShape, className?: string) {
  return cn(
    "inline-flex items-center justify-center transition-colors disabled:cursor-not-allowed disabled:opacity-50 aria-disabled:opacity-50",
    VARIANTS[variant],
    SIZES[size],
    SHAPES[shape],
    className
  );
}

/** A button's classes, for an element that looks like one but isn't a Button or ButtonLink. */
export function buttonClasses({
  variant = "primary",
  size = "md",
  shape = "pill",
  className,
}: { variant?: ButtonVariant; size?: ButtonSize; shape?: ButtonShape; className?: string } = {}): string {
  return classesFor(variant, size, shape, className);
}

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  shape?: ButtonShape;
}

export function Button({
  variant = "primary",
  size = "md",
  shape = "pill",
  className,
  type = "button",
  ...props
}: ButtonProps) {
  return <button type={type} className={classesFor(variant, size, shape, className)} {...props} />;
}

interface ButtonLinkProps extends React.ComponentProps<typeof Link> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  shape?: ButtonShape;
}

/// A link styled as a button — for navigation, where a <button> with router.push would
/// lose middle-click, open-in-new-tab and prefetching.
export function ButtonLink({
  variant = "primary",
  size = "md",
  shape = "pill",
  className,
  ...props
}: ButtonLinkProps) {
  return <Link className={classesFor(variant, size, shape, className)} {...props} />;
}
