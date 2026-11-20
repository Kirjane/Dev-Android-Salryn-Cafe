import {
  cloneElement,
  isValidElement,
  useId,
  type InputHTMLAttributes,
  type ReactElement,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from "react";

type ControlA11yProps = {
  id?: string;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
  required?: boolean;
};

export type FormFieldProps = {
  label: string;
  id?: string;
  hint?: string;
  error?: string | null;
  required?: boolean;
  children: ReactElement<ControlA11yProps>;
};

/**
 * Accessible form field guaranteeing programmatic <label htmlFor>, aria-invalid, and aria-describedby.
 */
export function FormField({ label, id, hint, error, required = false, children }: FormFieldProps) {
  const autoId = useId();
  const controlId = id ?? children.props.id ?? autoId;
  const hintId = hint && !error ? `${controlId}-hint` : undefined;
  const errorId = error ? `${controlId}-error` : undefined;
  const describedBy = [errorId, hintId, children.props["aria-describedby"]].filter(Boolean).join(" ") || undefined;

  const enhancedControl = isValidElement(children)
    ? cloneElement(children, {
        id: controlId,
        "aria-invalid": Boolean(error) || undefined,
        "aria-describedby": describedBy,
        required: required || children.props.required,
      })
    : children;

  return (
    <div className="salryn-field">
      <label htmlFor={controlId} className="salryn-field-label">
        {label}
        {required ? (
          <span className="salryn-field-required" aria-hidden="true">
            *
          </span>
        ) : null}
      </label>
      {enhancedControl}
      {hint && !error ? (
        <p id={hintId} className="salryn-field-hint">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} role="alert" className="salryn-field-error">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export function TextInput({ className = "", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={`salryn-input salryn-focusable ${className}`.trim()} {...props} />;
}

export function SelectInput({ className = "", ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className={`salryn-input salryn-focusable ${className}`.trim()} {...props} />;
}

export function TextareaInput({ className = "", ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className={`salryn-input salryn-focusable ${className}`.trim()} {...props} />;
}
