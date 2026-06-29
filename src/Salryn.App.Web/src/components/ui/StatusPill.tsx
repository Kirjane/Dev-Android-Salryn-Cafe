import type { ReactNode } from "react";
import { tonePill, type Tone } from "./tokens";

type StatusPillProps = {
  tone?: Tone;
  children: ReactNode;
  className?: string;
};

export function StatusPill({ tone = "neutral", children, className = "" }: StatusPillProps) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset ${tonePill[tone]} ${className}`}
    >
      {children}
    </span>
  );
}
