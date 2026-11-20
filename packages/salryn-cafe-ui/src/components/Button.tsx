import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";

export type ButtonVariant = "primary" | "accent" | "secondary" | "ghost" | "danger";
export type ButtonSize = "sm" | "md" | "lg";

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  leftIcon?: ReactNode;
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  {
    variant = "primary",
    size = "md",
    loading = false,
    leftIcon,
    type = "button",
    disabled,
    className = "",
    children,
    ...rest
  },
  ref,
) {
  const isDisabled = Boolean(disabled || loading);

  return (
    <button
      ref={ref}
      type={type}
      disabled={isDisabled}
      aria-busy={loading || undefined}
      className={`salryn-btn salryn-btn--${variant} salryn-btn--${size} salryn-focusable ${className}`.trim()}
      {...rest}
    >
      {leftIcon ? <span aria-hidden="true">{leftIcon}</span> : null}
      <span>{children}</span>
    </button>
  );
});
