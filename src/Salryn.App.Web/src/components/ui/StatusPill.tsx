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
      className={`inline-flex min-h-6 items-center gap-1.5 rounded-md px-2 py-0.5 text-xs font-semibold ring-1 ring-inset ${tonePill[tone]} ${className}`}
    >
      <span className="h-1.5 w-1.5 rounded-full bg-current opacity-75" aria-hidden="true" />
      {children}
    </span>
  );
}
