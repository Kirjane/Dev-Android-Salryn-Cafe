import type { ReactNode } from "react";
import { toneAlert, type Tone } from "./tokens";

type AlertProps = {
  tone?: Tone;
  title?: string;
  children?: ReactNode;
  className?: string;
};

export function Alert({ tone = "info", title, children, className = "" }: AlertProps) {
  return (
    <div className={`rounded-lg border px-4 py-3 text-sm ${toneAlert[tone]} ${className}`}>
      {title ? <p className="font-semibold">{title}</p> : null}
      {children ? <div className={title ? "mt-1" : ""}>{children}</div> : null}
    </div>
  );
}
