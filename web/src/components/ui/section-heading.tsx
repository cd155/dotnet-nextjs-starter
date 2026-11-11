import { cn } from "@/lib/utils";

/**
 * Two heading weights: a bold page-level heading above a list, and a quieter muted heading
 * above a single card. This names them rather than leaving each page to hand-roll the classes.
 */
type SectionHeadingVariant = "page" | "section";

const VARIANTS: Record<SectionHeadingVariant, string> = {
  page: "text-lg font-bold",
  section: "text-base font-bold text-muted/80",
};

interface SectionHeadingProps {
  variant?: SectionHeadingVariant;
  /**
   * Heading level. Defaults to `h2` for both variants because `variant` is about weight, not depth
   * — a quiet heading is still usually a top-level section under the page's `h1`. Pass `h3` only
   * where the section is genuinely nested inside another.
   */
  as?: "h2" | "h3";
  className?: string;
  children: React.ReactNode;
}

export function SectionHeading({
  variant = "page",
  as: Tag = "h2",
  className,
  children,
}: SectionHeadingProps) {
  return <Tag className={cn(VARIANTS[variant], className)}>{children}</Tag>;
}
