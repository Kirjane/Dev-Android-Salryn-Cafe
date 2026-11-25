import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { AlertCircle, CheckCircle2, Info, X, AlertTriangle } from "lucide-react";

/* ---------- Formatters ---------- */
export function formatMoney(cents: number): string {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(cents / 100);
}

export function formatQty(x1000: number, unit?: string): string {
  const val = x1000 / 1000;
  const formatted = new Intl.NumberFormat("en-PH", {
    maximumFractionDigits: 2,
  }).format(val);
  if (!unit) return formatted;
  const short =
    unit === "Milliliter"
      ? "mL"
      : unit === "Gram"
        ? "g"
        : unit === "Kilogram"
          ? "kg"
          : unit === "Liter"
            ? "L"
            : unit === "Piece"
              ? "pc"
              : unit;
  return `${formatted} ${short}`;
}

export function formatManilaTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  try {
    return new Intl.DateTimeFormat("en-PH", {
      timeZone: "Asia/Manila",
      month: "short",
      day: "numeric",
      hour: "numeric",
      minute: "2-digit",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

/* ---------- StatusPill ---------- */
export type PillTone = "success" | "warning" | "danger" | "info" | "neutral";

export function StatusPill({
  tone = "neutral",
  children,
}: {
  tone?: PillTone;
  children: ReactNode;
}) {
  return <span className={`r3-pill r3-pill--${tone}`}>{children}</span>;
}

/* ---------- PageHeader ---------- */
export function PageHeader({
  title,
  subtitle,
  actions,
  breadcrumb,
}: {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  breadcrumb?: ReactNode;
}) {
  return (
    <header className="r3-page-header">
      <div style={{ minWidth: 0 }}>
        {breadcrumb && (
          <div style={{ fontSize: 12.5, color: "var(--r3-ink-muted)", marginBottom: 4 }}>
            {breadcrumb}
          </div>
        )}
        <h1 className="r3-page-title">{title}</h1>
        {subtitle && <p className="r3-page-subtitle">{subtitle}</p>}
      </div>
      {actions && <div className="r3-page-actions">{actions}</div>}
    </header>
  );
}

/* ---------- AlertBanner ---------- */
export function AlertBanner({
  tone = "info",
  title,
  children,
  action,
}: {
  tone?: "info" | "success" | "warning" | "danger";
  title?: string;
  children: ReactNode;
  action?: ReactNode;
}) {
  const Icon =
    tone === "success"
      ? CheckCircle2
      : tone === "warning"
        ? AlertTriangle
        : tone === "danger"
          ? AlertCircle
          : Info;
  return (
    <div className={`r3-alert r3-alert--${tone}`} role={tone === "danger" ? "alert" : "status"}>
      <Icon size={18} style={{ flexShrink: 0, marginTop: 1 }} aria-hidden="true" />
      <div style={{ flex: 1, minWidth: 0 }}>
        {title && <div style={{ fontWeight: 700, marginBottom: 2 }}>{title}</div>}
        <div>{children}</div>
      </div>
      {action && <div style={{ flexShrink: 0 }}>{action}</div>}
    </div>
  );
}

/* ---------- EmptyStateCard ---------- */
export function EmptyStateCard({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div
      className="r3-card"
      style={{ padding: "32px 20px", textAlign: "center", display: "grid", gap: 8, justifyItems: "center" }}
    >
      <div style={{ fontSize: 16, fontWeight: 700 }}>{title}</div>
      <div style={{ fontSize: 13.5, color: "var(--r3-ink-secondary)", maxWidth: 440 }}>{description}</div>
      {action && <div style={{ marginTop: 8 }}>{action}</div>}
    </div>
  );
}

/* ---------- FlowStepper ---------- */
export function FlowStepper({
  steps,
  currentIndex,
  onSelectStep,
}: {
  steps: Array<{ key: string; label: string }>;
  currentIndex: number;
  onSelectStep?: (idx: number) => void;
}) {
  return (
    <nav className="r3-stepper" aria-label="Workflow steps">
      {steps.map((step, idx) => {
        const isCurrent = idx === currentIndex;
        const isDone = idx < currentIndex;
        return (
          <button
            key={step.key}
            type="button"
            className={`r3-step-btn ${isDone ? "r3-step-btn--done" : ""}`}
            aria-current={isCurrent ? "step" : undefined}
            onClick={() => onSelectStep?.(idx)}
          >
            <span className="r3-step-num">{isDone ? "✓" : idx + 1}</span>
            <span>{step.label}</span>
          </button>
        );
      })}
    </nav>
  );
}

/* ---------- Native <dialog> Modal & RightDrawer ---------- */
function useNativeDialog(
  open: boolean,
  onClose: () => void,
  preventBackdropDismiss = false,
) {
  const ref = useRef<HTMLDialogElement | null>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (open) {
      if (!el.open) {
        if (typeof el.showModal === "function") {
          el.showModal();
        } else {
          el.setAttribute("open", "");
        }
      }
    } else if (el.open) {
      if (typeof el.close === "function") {
        el.close();
      } else {
        el.removeAttribute("open");
      }
    }
  }, [open]);

  const handleCancel = useCallback(
    (e: React.SyntheticEvent<HTMLDialogElement>) => {
      e.preventDefault();
      if (!preventBackdropDismiss) onClose();
    },
    [onClose, preventBackdropDismiss],
  );

  const handleBackdropClick = useCallback(
    (e: React.MouseEvent<HTMLDialogElement>) => {
      if (preventBackdropDismiss) return;
      if (e.target === ref.current) {
        onClose();
      }
    },
    [onClose, preventBackdropDismiss],
  );

  return { ref, handleCancel, handleBackdropClick };
}

export function ModalDialog({
  open,
  title,
  onClose,
  children,
  footer,
  preventBackdropDismiss = false,
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  preventBackdropDismiss?: boolean;
}) {
  const { ref, handleCancel, handleBackdropClick } = useNativeDialog(
    open,
    onClose,
    preventBackdropDismiss,
  );
  if (!open) return null;
  return (
    <dialog
      ref={ref}
      className="r3-dialog"
      aria-label={title}
      onCancel={handleCancel}
      onClick={handleBackdropClick}
    >
      <div className="r3-dialog-header">
        <h2 className="r3-dialog-title">{title}</h2>
        <button
          type="button"
          className="r3-btn r3-btn--ghost r3-btn--sm"
          onClick={onClose}
          aria-label={`Close ${title}`}
        >
          <X size={18} />
        </button>
      </div>
      <div className="r3-dialog-body">{children}</div>
      {footer && <div className="r3-dialog-footer">{footer}</div>}
    </dialog>
  );
}

export function RightDrawer({
  open,
  title,
  onClose,
  children,
  footer,
  preventBackdropDismiss = false,
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  preventBackdropDismiss?: boolean;
}) {
  const { ref, handleCancel, handleBackdropClick } = useNativeDialog(
    open,
    onClose,
    preventBackdropDismiss,
  );
  if (!open) return null;
  return (
    <dialog
      ref={ref}
      className="r3-drawer"
      aria-label={title}
      onCancel={handleCancel}
      onClick={handleBackdropClick}
    >
      <div className="r3-dialog-header">
        <h2 className="r3-dialog-title">{title}</h2>
        <button
          type="button"
          className="r3-btn r3-btn--ghost r3-btn--sm"
          onClick={onClose}
          aria-label={`Close ${title}`}
        >
          <X size={18} />
        </button>
      </div>
      <div className="r3-dialog-body">{children}</div>
      {footer && <div className="r3-dialog-footer">{footer}</div>}
    </dialog>
  );
}

/* ---------- Toast Context ---------- */
type ToastItem = { id: string; message: string };
const ToastCtx = createContext<{ pushToast: (msg: string) => void }>({ pushToast: () => {} });

export function R3ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const pushToast = useCallback((message: string) => {
    const id = `${Date.now()}-${Math.random().toString(36).slice(2, 6)}`;
    setToasts((prev) => [...prev.slice(-1), { id, message }]);
    window.setTimeout(() => {
      setToasts((prev) => prev.filter((t) => t.id !== id));
    }, 4200);
  }, []);

  return (
    <ToastCtx.Provider value={{ pushToast }}>
      {children}
      <div className="r3-toast-stack" role="status" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className="r3-toast">
            <span>{t.message}</span>
            <button
              type="button"
              className="r3-btn r3-btn--ghost r3-btn--sm"
              style={{ color: "var(--r3-nav-ink)", minHeight: 28, padding: "0 6px" }}
              aria-label="Dismiss notification"
              onClick={() => setToasts((prev) => prev.filter((x) => x.id !== t.id))}
            >
              <X size={14} />
            </button>
          </div>
        ))}
      </div>
    </ToastCtx.Provider>
  );
}

export function useR3Toast() {
  return useContext(ToastCtx);
}
