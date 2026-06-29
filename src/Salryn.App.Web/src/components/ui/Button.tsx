import type { ButtonHTMLAttributes, ReactNode } from "react";
import { focusRing } from "./tokens";

type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";
type ButtonSize = "sm" | "md";

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  size?: ButtonSize;
  children: ReactNode;
};

const variantClasses: Record<ButtonVariant, string> = {
  primary: "bg-navy-900 text-white hover:bg-navy-800 disabled:opacity-60",
  secondary:
    "bg-white text-slate-700 border border-slate-300 hover:bg-slate-50 disabled:opacity-60",
  danger: "bg-red-700 text-white hover:bg-red-800 disabled:opacity-60",
  ghost: "bg-transparent text-slate-600 hover:bg-slate-100 disabled:opacity-60",
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: "px-3 py-1.5 text-xs",
  md: "px-4 py-2 text-sm",
};

export function Button({
  variant = "primary",
  size = "md",
  className = "",
  children,
  type = "button",
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={`inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-colors disabled:cursor-not-allowed ${variantClasses[variant]} ${sizeClasses[size]} ${focusRing} ${className}`}
      {...rest}
    >
      {children}
    </button>
  );
}
