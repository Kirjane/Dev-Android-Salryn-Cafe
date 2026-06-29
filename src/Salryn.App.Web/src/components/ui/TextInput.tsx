import type { InputHTMLAttributes } from "react";
import { focusRing } from "./tokens";

type TextInputProps = InputHTMLAttributes<HTMLInputElement>;

export function TextInput({ className = "", type = "text", ...rest }: TextInputProps) {
  return (
    <input
      type={type}
      className={`w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 ${focusRing} ${className}`}
      {...rest}
    />
  );
}
