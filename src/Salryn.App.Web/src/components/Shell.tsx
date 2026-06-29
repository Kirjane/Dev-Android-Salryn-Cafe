import type { ReactNode } from "react";
import type { SessionState } from "../api/session";

type ShellProps = {
  children: ReactNode;
  session: SessionState;
  onLogout: () => void;
};

const navItems = ["Dashboard", "Products", "Inventory", "Sales", "Users", "Settings"];

export function Shell({ children, session, onLogout }: ShellProps) {
  return (
    <div className="min-h-screen bg-slate-100">
      <header className="bg-navy-900 px-6 py-4 text-white shadow">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-xs uppercase tracking-[0.2em] text-slate-300">Salryn</p>
            <h1 className="text-xl font-semibold">Operator Console</h1>
          </div>
          <div className="text-right">
            <p className="text-sm text-slate-200">
              {session.user?.displayName} · {session.user?.roleName}
            </p>
            <button
              type="button"
              onClick={onLogout}
              className="mt-1 text-xs text-slate-300 underline hover:text-white"
            >
              Sign out
            </button>
          </div>
        </div>
      </header>

      <div className="flex min-h-[calc(100vh-72px)]">
        <aside className="w-56 bg-navy-800 px-4 py-6 text-white">
          <nav className="space-y-2">
            {navItems.map((item) => (
              <div
                key={item}
                className={`rounded-lg px-3 py-2 text-sm ${
                  item === "Dashboard"
                    ? "bg-navy-700 font-medium text-white"
                    : "text-slate-300"
                }`}
              >
                {item}
              </div>
            ))}
          </nav>
        </aside>

        <main className="flex-1 p-6">{children}</main>
      </div>
    </div>
  );
}
