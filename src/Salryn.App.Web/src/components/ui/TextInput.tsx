import type { InputHTMLAttributes } from "react";
import { focusRing } from "./tokens";

type TextInputProps = InputHTMLAttributes<HTMLInputElement>;

export function TextInput({ className = "", type = "text", ...rest }: TextInputProps) {
  return (
    <input
      type={type}
      className={`min-h-9 w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 shadow-sm placeholder:text-slate-400 disabled:cursor-not-allowed disabled:bg-slate-100 disabled:text-slate-400 ${focusRing} ${className}`}
      {...rest}
    />
  );
}
