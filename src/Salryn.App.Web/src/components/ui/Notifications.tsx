import {
  AlertCircle,
  AlertTriangle,
  CheckCircle2,
  Info,
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

export type NotificationTone = "success" | "error" | "warning" | "info";

type AppNotification = {
  id: number;
  tone: NotificationTone;
  title: string;
  description?: string;
};

type Notify = Record<NotificationTone, (title: string, description?: string) => void>;

type NotificationContextValue = {
  notify: Notify;
  dismiss: (id: number) => void;
};

const NotificationContext = createContext<NotificationContextValue | null>(null);
const maxVisibleNotifications = 4;
const durationByTone: Record<NotificationTone, number> = {
  success: 4000,
  info: 4500,
  warning: 7000,
  error: 9000,
};

const notificationStyles: Record<NotificationTone, { Icon: LucideIcon; classes: string }> = {
  success: { Icon: CheckCircle2, classes: "border-emerald-200 bg-emerald-50 text-emerald-950" },
  error: { Icon: AlertCircle, classes: "border-rose-200 bg-rose-50 text-rose-950" },
  warning: { Icon: AlertTriangle, classes: "border-amber-200 bg-amber-50 text-amber-950" },
  info: { Icon: Info, classes: "border-blue-200 bg-blue-50 text-blue-950" },
};

export function NotificationProvider({ children }: { children: ReactNode }) {
  const [notifications, setNotifications] = useState<AppNotification[]>([]);
  const nextId = useRef(0);
  const timers = useRef(new Map<number, number>());

  const dismiss = useCallback((id: number) => {
    const timer = timers.current.get(id);
    if (timer !== undefined) window.clearTimeout(timer);
    timers.current.delete(id);
    setNotifications((current) => current.filter((notification) => notification.id !== id));
  }, []);

  const add = useCallback((tone: NotificationTone, title: string, description?: string) => {
    const id = ++nextId.current;
    const notification: AppNotification = { id, tone, title, description };
    setNotifications((current) => [...current, notification].slice(-maxVisibleNotifications));
    const timer = window.setTimeout(() => dismiss(id), durationByTone[tone]);
    timers.current.set(id, timer);
  }, [dismiss]);

  useEffect(() => () => {
    for (const timer of timers.current.values()) window.clearTimeout(timer);
    timers.current.clear();
  }, []);

  const value = useMemo<NotificationContextValue>(() => ({
    dismiss,
    notify: {
      success: (title, description) => add("success", title, description),
      error: (title, description) => add("error", title, description),
      warning: (title, description) => add("warning", title, description),
      info: (title, description) => add("info", title, description),
    },
  }), [add, dismiss]);

  return (
    <NotificationContext.Provider value={value}>
      {children}
      <NotificationViewport notifications={notifications} onDismiss={dismiss} />
    </NotificationContext.Provider>
  );
}

export function useNotifications(): NotificationContextValue {
  const value = useContext(NotificationContext);
  if (!value) throw new Error("useNotifications must be used inside NotificationProvider.");
  return value;
}

export function NotificationViewport({
  notifications,
  onDismiss,
}: {
  notifications: AppNotification[];
  onDismiss: (id: number) => void;
}) {
  return (
    <div
      className="pointer-events-none fixed inset-x-0 top-[62px] z-[100] flex flex-col items-end gap-2 px-3 py-2 sm:left-auto sm:w-[25rem] sm:px-5"
      aria-live="polite"
      aria-relevant="additions"
      aria-label="Action notifications"
    >
      {notifications.map((notification) => {
        const { Icon, classes } = notificationStyles[notification.tone];
        return (
          <div
            key={notification.id}
            className={`pointer-events-auto flex w-full items-start gap-3 rounded-lg border px-4 py-3 shadow-lg ${classes}`}
            role={notification.tone === "error" ? "alert" : "status"}
          >
            <Icon className="mt-0.5 shrink-0" size={18} aria-hidden="true" />
            <div className="min-w-0 flex-1">
              <p className="text-sm font-semibold">{notification.title}</p>
              {notification.description ? (
                <p className="mt-1 break-words text-sm opacity-80">{notification.description}</p>
              ) : null}
            </div>
            <button
              type="button"
              className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md hover:bg-black/5 focus:outline-none focus-visible:ring-2 focus-visible:ring-current"
              onClick={() => onDismiss(notification.id)}
              aria-label={`Dismiss ${notification.title}`}
            >
              <X size={16} aria-hidden="true" />
            </button>
          </div>
        );
      })}
    </div>
  );
}
