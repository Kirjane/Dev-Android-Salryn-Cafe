import { X } from "lucide-react";
import { useEffect, useRef, type ReactNode } from "react";
import { Button } from "./Button";

type DialogSize = "sm" | "md" | "lg" | "xl" | "2xl";

type DialogProps = {
  open: boolean;
  title: string;
  subtitle?: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  onClose: () => void;
  footer?: ReactNode;
  size?: DialogSize;
};

const sizeClasses: Record<DialogSize, string> = {
  sm: "max-w-md",
  md: "max-w-lg",
  lg: "max-w-xl",
  xl: "max-w-2xl",
  "2xl": "max-w-3xl",
};

export function Dialog({
  open,
  title,
  subtitle,
  description,
  children,
  onClose,
  footer,
  size = "md",
}: DialogProps) {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    if (!open) return;

    previousFocusRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.stopPropagation();
        onCloseRef.current();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
      if (previousFocusRef.current && document.contains(previousFocusRef.current)) {
        previousFocusRef.current.focus();
      }
    };
  }, [open]);

  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div
        className="absolute inset-0 bg-slate-900/55"
        onClick={onClose}
        aria-hidden="true"
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`relative z-10 max-h-[calc(100vh-2rem)] w-full min-w-0 ${sizeClasses[size]} overflow-x-hidden overflow-y-auto rounded-lg border border-slate-300 bg-white shadow-xl`}
      >
        <div className="flex min-h-13 items-start justify-between gap-4 bg-navy-900 px-5 py-3 text-white">
          <div className="min-w-0">
            <h3 className="text-base font-semibold leading-snug">{title}</h3>
            {subtitle ? <p className="mt-0.5 text-xs text-slate-300">{subtitle}</p> : null}
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded p-1 text-slate-300 hover:bg-white/10 hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
            aria-label="Close dialog"
          >
            <X aria-hidden="true" size={20} />
          </button>
        </div>
        {description || children ? (
          <div className="min-w-0 space-y-4 px-4 py-5 sm:px-6">
            {description ? <div className="text-sm leading-6 text-slate-600">{description}</div> : null}
            {children}
          </div>
        ) : null}
        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-200 bg-slate-50 px-4 py-3.5 sm:px-5">
          {footer ?? (
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  description?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
};

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  destructive = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      title={title}
      description={description}
      onClose={onCancel}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel}>
            {cancelLabel}
          </Button>
          <Button variant={destructive ? "danger" : "primary"} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </>
      }
    />
  );
}
