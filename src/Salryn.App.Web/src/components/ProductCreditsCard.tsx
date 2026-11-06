/**
 * Legacy component export retained for terminal activation compatibility.
 * Client-facing content is product identity only; no personal credits or org chart.
 */
type ProductCreditsCardProps = {
  variant?: "card" | "integrated";
};

const company = "Kirjane Information Technology Services";
const supportEmail = "salryn.info@gmail.com";

export function ProductCreditsCard({ variant = "card" }: ProductCreditsCardProps) {
  if (variant === "integrated") {
    return (
      <section className="border-t border-white/10 py-10" aria-label="About Salryn">
        <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-cyan-300">Salryn POS</p>
        <h2 className="mt-3 text-2xl font-semibold text-white">A product of {company}</h2>
        <p className="mt-3 max-w-xl text-sm leading-7 text-slate-300">
          Local-first point-of-sale software for store operations. For license and product support,
          contact <span className="font-medium text-cyan-200">{supportEmail}</span>.
        </p>
        <p className="mt-6 text-xs text-slate-400">© 2026 {company}. All rights reserved.</p>
      </section>
    );
  }

  return (
    <section
      className="flex h-full flex-col overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-md shadow-slate-200/60"
      aria-label="About Salryn"
    >
      <div className="bg-gradient-to-br from-navy-900 via-navy-800 to-blue-900 px-6 py-8 text-white">
        <p className="text-[10px] font-semibold uppercase tracking-[0.3em] text-emerald-300">Salryn POS</p>
        <h2 className="mt-3 text-3xl font-black tracking-[0.1em]">SALRYN</h2>
        <p className="mt-3 text-sm font-semibold text-blue-100">A product of {company}</p>
      </div>
      <div className="flex flex-1 flex-col gap-4 px-6 py-6">
        <p className="text-sm leading-6 text-slate-700">
          Salryn supports day-to-day sales and local store operations. License activation and
          support are provided through the product support channel.
        </p>
        <p className="text-sm leading-6 text-slate-600">
          LAN Terminals are a future feature and are not included in this production release.
        </p>
        <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Product support</p>
          <p className="mt-1 break-all text-sm font-semibold text-navy-900">{supportEmail}</p>
        </div>
        <footer className="mt-auto border-t border-slate-100 pt-4 text-xs text-slate-500">
          © 2026 {company}. All rights reserved.
        </footer>
      </div>
    </section>
  );
}
