import type { ButtonHTMLAttributes, ReactNode } from "react";
import { focusRing } from "./tokens";

type ButtonVariant = "primary" | "secondary" | "success" | "danger" | "ghost";
type ButtonSize = "sm" | "md" | "lg" | "icon";

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  size?: ButtonSize;
  children: ReactNode;
};

const variantClasses: Record<ButtonVariant, string> = {
  primary: "border border-blue-600 bg-blue-600 text-white shadow-sm hover:border-blue-700 hover:bg-blue-700 disabled:opacity-60",
  secondary:
    "border border-slate-300 bg-white text-slate-700 shadow-sm hover:border-slate-400 hover:bg-slate-50 disabled:opacity-60",
  success: "border border-green-600 bg-green-600 text-white shadow-sm hover:border-green-700 hover:bg-green-700 disabled:opacity-60",
  danger: "border border-red-600 bg-red-600 text-white shadow-sm hover:border-red-700 hover:bg-red-700 disabled:opacity-60",
  ghost: "bg-transparent text-slate-600 hover:bg-slate-100 disabled:opacity-60",
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: "min-h-8 px-3 py-1.5 text-xs",
  md: "min-h-9 px-4 py-2 text-sm",
  lg: "min-h-11 px-5 py-2.5 text-sm",
  icon: "h-9 w-9 p-0",
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
      className={`inline-flex shrink-0 items-center justify-center gap-2 rounded-md font-semibold transition-colors disabled:cursor-not-allowed ${variantClasses[variant]} ${sizeClasses[size]} ${focusRing} ${className}`}
      {...rest}
    >
      {children}
    </button>
  );
}
