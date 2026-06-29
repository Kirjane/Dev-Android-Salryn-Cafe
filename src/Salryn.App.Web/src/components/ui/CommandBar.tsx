import type { ReactNode } from "react";

type CommandBarProps = {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  status?: ReactNode;
};

export function CommandBar({ title, subtitle, actions, status }: CommandBarProps) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4 border-b border-slate-100 pb-4">
      <div>
        <h2 className="text-2xl font-semibold text-slate-900">{title}</h2>
        {subtitle ? <p className="mt-1 text-sm text-slate-500">{subtitle}</p> : null}
        {status ? <div className="mt-2">{status}</div> : null}
      </div>
      {actions ? <div className="flex items-center gap-2">{actions}</div> : null}
    </div>
  );
}
