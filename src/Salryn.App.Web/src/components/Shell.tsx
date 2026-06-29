import type { ReactNode } from "react";

type ShellProps = {
  children: ReactNode;
};

const navItems = ["Dashboard", "Products", "Inventory", "Sales", "Users", "Settings"];

export function Shell({ children }: ShellProps) {
  return (
    <div className="min-h-screen bg-slate-100">
      <header className="bg-navy-900 px-6 py-4 text-white shadow">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-xs uppercase tracking-[0.2em] text-slate-300">Salryn</p>
            <h1 className="text-xl font-semibold">Operator Console</h1>
          </div>
          <p className="text-sm text-slate-300">WEB-A1 proof dashboard</p>
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
