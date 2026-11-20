import {
  AlertCircle,
  AlertTriangle,
  CheckCircle2,
  Coffee,
  Info,
  MinusCircle,
  type LucideIcon,
} from "lucide-react";
import type { ReactNode } from "react";

export type StatusTone = "neutral" | "success" | "warning" | "danger" | "info" | "copper";

const TONE_ICONS: Record<StatusTone, LucideIcon> = {
  neutral: MinusCircle,
  success: CheckCircle2,
  warning: AlertTriangle,
  danger: AlertCircle,
  info: Info,
  copper: Coffee,
};

export type StatusBadgeProps = {
  tone?: StatusTone;
  children: ReactNode;
  className?: string;
};

/**
 * Semantic status pill with paired icon + text so state is never communicated by color alone.
 */
export function StatusBadge({ tone = "neutral", children, className = "" }: StatusBadgeProps) {
  const Icon = TONE_ICONS[tone];
  return (
    <span className={`salryn-badge salryn-badge--${tone} ${className}`.trim()} data-tone={tone}>
      <Icon aria-hidden="true" size={14} strokeWidth={2.25} />
      <span>{children}</span>
    </span>
  );
}
