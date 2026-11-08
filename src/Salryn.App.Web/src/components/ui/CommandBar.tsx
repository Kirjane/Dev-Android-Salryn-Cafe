import type { ReactNode } from "react";

type CommandBarProps = {
  title: string;
  subtitle?: string;
  eyebrow?: string;
  actions?: ReactNode;
  status?: ReactNode;
  compact?: boolean;
};

export function CommandBar({ title, subtitle, eyebrow, actions, status, compact = false }: CommandBarProps) {
  return (
    <div className={`flex min-w-0 flex-wrap items-start justify-between border-b border-slate-200 ${compact ? "gap-2 pb-2" : "gap-4 pb-3.5"}`}>
      <div className="min-w-0">
        {eyebrow ? (
          <p className="mb-0.5 text-[11px] font-semibold uppercase tracking-wider text-blue-700">{eyebrow}</p>
        ) : null}
        <h1 className={`${compact ? "text-xl" : "text-2xl"} break-words font-bold tracking-tight text-slate-950`}>{title}</h1>
        {subtitle ? <p className={`${compact ? "mt-0.5 text-xs" : "mt-1 text-sm"} break-words text-slate-500`}>{subtitle}</p> : null}
        {status ? <div className={compact ? "mt-1.5" : "mt-2"}>{status}</div> : null}
      </div>
      {actions ? <div className="flex max-w-full flex-wrap items-center gap-2">{actions}</div> : null}
    </div>
  );
}
