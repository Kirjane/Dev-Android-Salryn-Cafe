import {
  AlertCircle,
  AlertTriangle,
  CheckCircle2,
  Info,
  Loader2,
  X,
  type LucideIcon,
} from "lucide-react";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

export type FeedbackTone = "success" | "warning" | "danger" | "info";

const ICONS: Record<FeedbackTone, LucideIcon> = {
  success: CheckCircle2,
  warning: AlertTriangle,
  danger: AlertCircle,
  info: Info,
};

export type AlertProps = {
  tone: FeedbackTone;
  title: string;
  description?: string;
  onDismiss?: () => void;
};

export function Alert({ tone, title, description, onDismiss }: AlertProps) {
  const Icon = ICONS[tone];
  const role = tone === "danger" || tone === "warning" ? "alert" : "status";

  return (
    <div role={role} className={`salryn-alert salryn-alert--${tone}`}>
      <Icon aria-hidden="true" size={18} style={{ flexShrink: 0, marginTop: 2 }} />
      <div style={{ flex: 1, minWidth: 0 }}>
        <p style={{ margin: 0, fontWeight: 700 }}>{title}</p>
        {description ? <p style={{ margin: "2px 0 0" }}>{description}</p> : null}
      </div>
      {onDismiss ? (
        <button
          type="button"
          onClick={onDismiss}
          aria-label={`Dismiss ${title}`}
          className="salryn-focusable"
          style={{
            background: "transparent",
            border: "none",
            color: "inherit",
            cursor: "pointer",
            minWidth: 32,
            minHeight: 32,
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            borderRadius: 6,
          }}
        >
          <X aria-hidden="true" size={16} />
        </button>
      ) : null}
    </div>
  );
}

type ToastItem = {
  id: number;
  tone: FeedbackTone;
  title: string;
  description?: string;
};

type ToastContextValue = {
  notify: (tone: FeedbackTone, title: string, description?: string) => void;
  dismiss: (id: number) => void;
};

const ToastContext = createContext<ToastContextValue | null>(null);

const AUTO_DISMISS_MS: Record<FeedbackTone, number> = {
  success: 4000,
  info: 4500,
  warning: 6500,
  danger: 8500,
};

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const nextId = useRef(0);
  const timers = useRef(new Map<number, number>());

  const dismiss = useCallback((id: number) => {
    const timer = timers.current.get(id);
    if (timer !== undefined) {
      window.clearTimeout(timer);
      timers.current.delete(id);
    }
    setToasts((prev) => prev.filter((item) => item.id !== id));
  }, []);

  const notify = useCallback(
    (tone: FeedbackTone, title: string, description?: string) => {
      const id = ++nextId.current;
      setToasts((prev) => [...prev, { id, tone, title, description }].slice(-4));
      const timer = window.setTimeout(() => dismiss(id), AUTO_DISMISS_MS[tone]);
      timers.current.set(id, timer);
    },
    [dismiss],
  );

  useEffect(
    () => () => {
      for (const timer of timers.current.values()) {
        window.clearTimeout(timer);
      }
      timers.current.clear();
    },
    [],
  );

  const value = useMemo(() => ({ notify, dismiss }), [notify, dismiss]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div
        role="region"
        className="salryn-toast-viewport"
        aria-live="polite"
        aria-relevant="additions"
        aria-label="Notifications"
      >
        {toasts.map((toast) => (
          <div key={toast.id} className="salryn-toast">
            <Alert
              tone={toast.tone}
              title={toast.title}
              description={toast.description}
              onDismiss={() => dismiss(toast.id)}
            />
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error("useToast must be used inside ToastProvider.");
  }
  return ctx;
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div
      className="salryn-card"
      style={{ padding: "32px 20px", textAlign: "center" }}
    >
      <p style={{ margin: 0, fontWeight: 700, color: "var(--salryn-espresso)" }}>{title}</p>
      {description ? (
        <p style={{ margin: "6px auto 0", maxWidth: 420, fontSize: "0.875rem", color: "var(--salryn-mocha)" }}>
          {description}
        </p>
      ) : null}
      {action ? <div style={{ marginTop: 16 }}>{action}</div> : null}
    </div>
  );
}

export function LoadingState({ label = "Loading..." }: { label?: string }) {
  return (
    <div
      role="status"
      aria-live="polite"
      className="salryn-card"
      style={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 10, padding: "28px 20px" }}
    >
      <Loader2 aria-hidden="true" size={18} />
      <span style={{ fontWeight: 600, color: "var(--salryn-espresso)" }}>{label}</span>
    </div>
  );
}
