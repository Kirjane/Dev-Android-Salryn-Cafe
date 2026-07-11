import type { ReactNode } from "react";
import { StatusPill } from "./StatusPill";
import type { Tone } from "./tokens";

type MetricCardProps = {
  title: string;
  value: string;
  subtitle?: string;
  tone?: Tone;
  badge?: ReactNode;
  emphasis?: boolean;
};

export function MetricCard({
  title,
  value,
  subtitle,
  tone = "neutral",
  badge,
  emphasis = false,
}: MetricCardProps) {
  return (
    <div
      className={`rounded-lg border bg-white p-4 shadow-card ${
        emphasis ? "border-amber-300 ring-1 ring-amber-200" : "border-slate-200"
      }`}
    >
      <div className="flex items-start justify-between gap-2">
        <p className="text-xs font-semibold text-slate-500">{title}</p>
        {badge ? <StatusPill tone={tone}>{badge}</StatusPill> : null}
      </div>
      <p className="mt-2 text-2xl font-bold text-slate-950">{value}</p>
      {subtitle ? <p className="mt-1 text-xs text-slate-400">{subtitle}</p> : null}
    </div>
  );
}
