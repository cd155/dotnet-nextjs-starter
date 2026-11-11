import { cn } from "@/lib/utils";

type PillTone = "blue" | "outline";

const TONES: Record<PillTone, string> = {
  blue: "bg-primary/10 text-primary",
  outline: "border border-card-border text-muted",
};

interface PillProps {
  tone?: PillTone;
  className?: string;
  children: React.ReactNode;
}

export function Pill({ tone = "blue", className, children }: PillProps) {
  return (
    <span className={cn("rounded-full px-2 py-0.5 text-xs font-semibold", TONES[tone], className)}>
      {children}
    </span>
  );
}
