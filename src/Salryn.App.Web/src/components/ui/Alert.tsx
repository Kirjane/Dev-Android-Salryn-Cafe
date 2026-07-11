import { AlertCircle, AlertTriangle, CheckCircle2, Info } from "lucide-react";
import type { ReactNode } from "react";
import { toneAlert, type Tone } from "./tokens";

type AlertProps = {
  tone?: Tone;
  title?: string;
  children?: ReactNode;
  className?: string;
};

export function Alert({ tone = "info", title, children, className = "" }: AlertProps) {
  const Icon = tone === "danger"
    ? AlertCircle
    : tone === "warning"
      ? AlertTriangle
      : tone === "success"
        ? CheckCircle2
        : Info;

  return (
    <div className={`flex items-start gap-3 rounded-md border px-4 py-3 text-sm ${toneAlert[tone]} ${className}`}>
      <Icon aria-hidden="true" className="mt-0.5 shrink-0" size={18} />
      <div className="min-w-0">
        {title ? <p className="font-semibold">{title}</p> : null}
        {children ? <div className={title ? "mt-1" : ""}>{children}</div> : null}
      </div>
    </div>
  );
}
