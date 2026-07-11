import type { ReactNode } from "react";

type CommandBarProps = {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  status?: ReactNode;
};

export function CommandBar({ title, subtitle, actions, status }: CommandBarProps) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4 border-b border-slate-200 pb-4">
      <div>
        <h1 className="text-2xl font-bold text-slate-950">{title}</h1>
        {subtitle ? <p className="mt-1 text-sm text-slate-500">{subtitle}</p> : null}
        {status ? <div className="mt-2">{status}</div> : null}
      </div>
      {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
    </div>
  );
}
