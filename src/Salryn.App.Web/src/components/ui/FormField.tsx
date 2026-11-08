import type { ReactNode } from "react";

type FormFieldProps = {
  label: string;
  htmlFor?: string;
  error?: string | null;
  hint?: string;
  required?: boolean;
  children: ReactNode;
};

export function FormField({ label, htmlFor, error, hint, required = false, children }: FormFieldProps) {
  return (
    <div className="block">
      <label htmlFor={htmlFor} className="text-sm font-semibold text-slate-700">
        {label}{required ? <span className="ml-1 text-red-600">*</span> : null}
      </label>
      <div className={`mt-1 ${error ? "[&>input]:border-red-500 [&>select]:border-red-500 [&>textarea]:border-red-500" : ""}`}>{children}</div>
      {hint && !error ? <p className="mt-1 text-xs text-slate-400">{hint}</p> : null}
      {error ? <p className="mt-1 text-xs font-medium text-red-600">{error}</p> : null}
    </div>
  );
}
