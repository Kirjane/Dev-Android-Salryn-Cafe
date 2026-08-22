import { useState, type InputHTMLAttributes } from "react";
import { Eye, EyeOff } from "lucide-react";
import { focusRing } from "./tokens";

type PasswordInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, "type">;

export function PasswordInput({ className = "", ...rest }: PasswordInputProps) {
  const [visible, setVisible] = useState(false);

  return (
    <div className="relative">
      <input
        type={visible ? "text" : "password"}
        className={`min-h-9 w-full rounded-md border border-slate-300 bg-white px-3 py-2 pr-11 text-sm text-slate-900 shadow-sm placeholder:text-slate-400 disabled:bg-slate-100 disabled:text-slate-400 ${focusRing} ${className}`}
        {...rest}
      />
      <button
        type="button"
        onClick={() => setVisible((current) => !current)}
        className="absolute inset-y-0 right-2 my-auto flex h-7 w-7 items-center justify-center rounded text-slate-500 hover:bg-slate-100 hover:text-slate-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-600"
        aria-label={visible ? "Hide password" : "Show password"}
      >
        {visible ? <EyeOff aria-hidden="true" size={17} /> : <Eye aria-hidden="true" size={17} />}
      </button>
    </div>
  );
}
