import { X } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
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
  busy?: boolean;
};

const sizeClasses: Record<DialogSize, string> = {
  sm: "max-w-md",
  md: "max-w-lg",
  lg: "max-w-xl",
  xl: "max-w-2xl",
  "2xl": "max-w-3xl",
};

// Global modal stack to coordinate inert backgrounds, scroll lock, and Escape handling
interface DialogStackItem {
  id: string;
  container: HTMLDivElement;
  inertElements: HTMLElement[];
  onClose: () => void;
  isBusy: () => boolean;
}

const dialogStack: DialogStackItem[] = [];
const inertReferenceCounts = new Map<HTMLElement, number>();
const initiallyInertElements = new WeakSet<HTMLElement>();
let originalBodyOverflow = "";

function acquireInert(element: HTMLElement) {
  const count = inertReferenceCounts.get(element) ?? 0;
  if (count === 0 && element.hasAttribute("inert")) initiallyInertElements.add(element);
  inertReferenceCounts.set(element, count + 1);
  element.setAttribute("data-dialog-inert", "true");
  element.setAttribute("inert", "");
}

function releaseInert(element: HTMLElement) {
  const count = inertReferenceCounts.get(element) ?? 0;
  if (count > 1) {
    inertReferenceCounts.set(element, count - 1);
    return;
  }
  inertReferenceCounts.delete(element);
  element.removeAttribute("data-dialog-inert");
  if (!initiallyInertElements.has(element)) element.removeAttribute("inert");
}

function getBackgroundElementsToInert(container: HTMLElement): HTMLElement[] {
  const elements: HTMLElement[] = [];
  let curr: HTMLElement | null = container;
  while (curr && curr !== document.body) {
    const parent: HTMLElement | null = curr.parentElement;
    if (parent) {
      for (const sibling of Array.from(parent.children)) {
        if (
          sibling !== curr &&
          sibling instanceof HTMLElement &&
          !sibling.hasAttribute("data-notification-viewport")
        ) {
          elements.push(sibling);
        }
      }
    }
    curr = parent;
  }
  return elements;
}

function pushDialog(item: Omit<DialogStackItem, "inertElements">) {
  if (dialogStack.length === 0) {
    originalBodyOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    document.body.dataset.modalOpen = "true";
  } else {
    // If a dialog is already open, make the previous dialog container inert
    const prev = dialogStack[dialogStack.length - 1];
    acquireInert(prev.container);
  }

  const inertElements = [...new Set(getBackgroundElementsToInert(item.container))];
  for (const el of inertElements) {
    acquireInert(el);
  }

  dialogStack.push({ ...item, inertElements });
}

function popDialog(id: string) {
  const index = dialogStack.findIndex((item) => item.id === id);
  if (index !== -1) {
    const [removed] = dialogStack.splice(index, 1);
    for (const el of removed.inertElements) {
      releaseInert(el);
    }
  }
  if (dialogStack.length === 0) {
    document.body.style.overflow = originalBodyOverflow;
    delete document.body.dataset.modalOpen;
  } else {
    // Topmost dialog is active and not inert
    const top = dialogStack[dialogStack.length - 1];
    releaseInert(top.container);
  }
}

export function Dialog({
  open,
  title,
  subtitle,
  description,
  children,
  onClose,
  footer,
  size = "md",
  busy = false,
}: DialogProps) {
  const [dialogId] = useState(() => "salryn-dialog-" + Math.random().toString(36).substring(2, 9));
  const containerRef = useRef<HTMLDivElement | null>(null);
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;
  const busyRef = useRef(busy);
  busyRef.current = busy;

  useEffect(() => {
    if (!open) return;

    // Capture origin focus for restoration upon close
    previousFocusRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;

    const container = containerRef.current;
    if (container) {
      pushDialog({
        id: dialogId,
        container,
        onClose: () => onCloseRef.current(),
        isBusy: () => busyRef.current,
      });
    }

    // Set initial focus
    const focusTimer = requestAnimationFrame(() => {
      if (!dialogRef.current) return;
      const focusable = dialogRef.current.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
      );
      const requestedInitialFocus = dialogRef.current.querySelector<HTMLElement>("[data-dialog-initial-focus]");
      if (requestedInitialFocus) {
        requestedInitialFocus.focus();
      } else if (focusable.length > 0) {
        focusable[0].focus();
      } else {
        dialogRef.current.focus();
      }
    });

    function handleKeyDown(event: KeyboardEvent) {
      // Only topmost dialog handles keydown
      const top = dialogStack[dialogStack.length - 1];
      if (top?.id !== dialogId) return;

      if (event.key === "Escape") {
        if (!busyRef.current) {
          event.stopPropagation();
          event.preventDefault();
          onCloseRef.current();
        } else {
          event.stopPropagation();
          event.preventDefault();
        }
      } else if (event.key === "Tab" && dialogRef.current) {
        const focusable = dialogRef.current.querySelectorAll<HTMLElement>(
          'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length > 0) {
          const first = focusable[0];
          const last = focusable[focusable.length - 1];
          if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
          }
        }
      }
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => {
      cancelAnimationFrame(focusTimer);
      window.removeEventListener("keydown", handleKeyDown);
      popDialog(dialogId);
      if (previousFocusRef.current && document.contains(previousFocusRef.current)) {
        previousFocusRef.current.focus();
      }
    };
  }, [open, dialogId]);

  if (!open) {
    return null;
  }

  function handleBackdropClick() {
    if (!busy) {
      onClose();
    }
  }

  const dialogContent = (
    <div
      ref={containerRef}
      data-dialog-container="true"
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
    >
      <div
        className="absolute inset-0 bg-slate-900/55"
        onClick={handleBackdropClick}
        aria-hidden="true"
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        aria-busy={busy ? "true" : undefined}
        tabIndex={-1}
        className={`relative z-10 max-h-[calc(100vh-2rem)] max-sm:max-h-[calc(100vh-6rem)] w-full min-w-0 ${sizeClasses[size]} overflow-x-hidden overflow-y-auto rounded-lg border border-slate-300 bg-white shadow-xl focus:outline-none`}
      >
        <div className="flex min-h-13 items-start justify-between gap-4 bg-navy-900 px-5 py-3 text-white">
          <div className="min-w-0">
            <h3 className="text-base font-semibold leading-snug">{title}</h3>
            {subtitle ? <p className="mt-0.5 text-xs text-slate-300">{subtitle}</p> : null}
          </div>
          <button
            type="button"
            disabled={busy}
            onClick={() => {
              if (!busy) onClose();
            }}
            className="rounded p-1 text-slate-300 hover:bg-white/10 hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-white disabled:pointer-events-none disabled:opacity-50"
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
            <Button variant="secondary" disabled={busy} onClick={onClose}>
              Close
            </Button>
          )}
        </div>
      </div>
    </div>
  );

  return createPortal(dialogContent, document.body);
}

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  description?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  busy?: boolean;
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
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      title={title}
      description={description}
      busy={busy}
      onClose={onCancel}
      footer={
        <>
          <Button variant="secondary" disabled={busy} onClick={onCancel}>
            {cancelLabel}
          </Button>
          <Button variant={destructive ? "danger" : "primary"} disabled={busy} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </>
      }
    />
  );
}
