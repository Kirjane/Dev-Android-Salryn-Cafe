import { useState, type InputHTMLAttributes } from "react";
import { focusRing } from "./tokens";

type PasswordInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, "type">;

export function PasswordInput({ className = "", ...rest }: PasswordInputProps) {
  const [visible, setVisible] = useState(false);

  return (
    <div className="relative">
      <input
        type={visible ? "text" : "password"}
        className={`w-full rounded-lg border border-slate-300 px-3 py-2 pr-16 text-sm text-slate-900 placeholder:text-slate-400 ${focusRing} ${className}`}
        {...rest}
      />
      <button
        type="button"
        onClick={() => setVisible((current) => !current)}
        className="absolute inset-y-0 right-2 my-auto h-6 rounded px-2 text-xs font-medium text-slate-500 hover:text-slate-700"
        aria-label={visible ? "Hide password" : "Show password"}
      >
        {visible ? "Hide" : "Show"}
      </button>
    </div>
  );
}
