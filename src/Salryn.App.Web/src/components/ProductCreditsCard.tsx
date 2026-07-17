/**
 * PRE-BETA: product website–style credits / identity rail for activation landing.
 * Credits-first — no admin-style metadata boxes.
 */
type ProductCreditsCardProps = {
  variant?: "card" | "integrated";
};

export function ProductCreditsCard({ variant = "card" }: ProductCreditsCardProps) {
  if (variant === "integrated") {
    return <IntegratedCredits />;
  }

  return (
    <section
      className="flex h-full flex-col overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-md shadow-slate-200/60"
      aria-label="Licensing and Credits"
    >
      <div className="relative overflow-hidden border-b border-slate-100 bg-gradient-to-br from-navy-900 via-navy-800 to-blue-900 px-6 py-8 text-white">
        <div className="pointer-events-none absolute -right-8 -top-8 h-28 w-28 rounded-full border border-white/10" aria-hidden />
        <div className="pointer-events-none absolute -bottom-10 left-8 h-24 w-24 rounded-full border border-emerald-400/20" aria-hidden />
        <p className="text-[10px] font-semibold uppercase tracking-[0.32em] text-emerald-300">Kirjane Labs</p>
        <h2 className="mt-3 text-4xl font-black tracking-[0.16em]">SALRYN</h2>
        <p className="mt-2 text-sm font-medium text-blue-100">by Kirjane Labs</p>
        <p className="mt-5 text-sm font-semibold tracking-wide text-emerald-200">{"Licensing & Credits"}</p>
        <p className="mt-2 text-xs font-medium text-white/75">A Kirjane Labs product</p>
      </div>

      <div className="flex flex-1 flex-col gap-5 px-5 py-5 sm:px-6">
        <div>
          <h3 className="text-center text-xs font-bold uppercase tracking-[0.22em] text-navy-900">Credits</h3>
          <p className="mt-1 text-center text-[11px] font-semibold tracking-wide text-slate-500">Kirjane Labs</p>
          <div className="mx-auto mt-2 h-3 w-px bg-slate-300" aria-hidden />
          <div className="mx-auto h-px w-[85%] max-w-sm bg-slate-300" aria-hidden />

          <div className="mt-4 grid gap-3 sm:grid-cols-3">
            <OrgNode
              role="Business Lead"
              name="Paul Gamayot"
              reports={[{ role: "Business Development", name: "James Karl Tara" }]}
            />
            <OrgNode
              role="Technology Lead"
              name="Kirch Ivan Balite"
              reports={[{ role: "Dev Ops Lead", name: "Osiris Kedigadash Palac" }]}
            />
            <OrgNode role="Marketing Lead" name="Jesse Edwin Culas" />
          </div>
        </div>

        <div className="rounded-xl border border-slate-200 bg-slate-50/90 px-4 py-3 text-center">
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Support</p>
          <p className="mt-1 break-all font-mono text-sm font-semibold text-navy-900">salryn.info@gmail.com</p>
          <p className="mt-2 text-[11px] leading-relaxed text-slate-500">
            Licensing, maintenance, deployment, and updates.
          </p>
        </div>

        <footer className="mt-auto border-t border-slate-100 pt-4 text-center text-[11px] leading-relaxed text-slate-500">
          <p>© 2026 Kirjane Labs. All rights reserved.</p>
          <p className="mt-1 text-[10px] text-slate-400">Repository-backed beta build</p>
        </footer>
      </div>
    </section>
  );
}

function IntegratedCredits() {
  return (
    <section className="relative overflow-hidden border-t border-white/10 py-14 sm:py-16" aria-label="Licensing and Credits">
      <div className="absolute -right-20 top-8 h-52 w-52 rounded-full border border-violet-300/10" aria-hidden />
      <div className="absolute -right-8 top-20 h-36 w-36 rounded-full border border-cyan-300/10" aria-hidden />

      <div className="relative grid gap-10 lg:grid-cols-[0.7fr_1.3fr] lg:gap-16">
        <div>
          <p className="text-[10px] font-bold uppercase tracking-[0.26em] text-cyan-300">Licensing & Credits</p>
          <h2 className="mt-4 text-3xl font-semibold tracking-[-0.03em] text-white sm:text-4xl">Built by Kirjane Labs.</h2>
          <p className="mt-4 max-w-md text-sm leading-7 text-slate-400">
            SALRYN is a Kirjane Labs product shaped around clear local operations, controlled deployment, and direct support.
          </p>
          <div className="mt-6">
            <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-slate-500">Salryn Infodesk</p>
            <p className="mt-2 break-all font-mono text-sm font-semibold text-cyan-200">salryn.info@gmail.com</p>
          </div>
        </div>

        <div>
          <div className="flex items-center justify-between gap-4">
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-slate-500">Credits</p>
            <p className="text-[10px] font-semibold tracking-[0.16em] text-slate-600">A Kirjane Labs product</p>
          </div>
          <div className="mt-5 grid gap-3 sm:grid-cols-3">
            <IntegratedOrgNode
              role="Business Lead"
              name="Paul Gamayot"
              reportRole="Business Development"
              reportName="James Karl Tara"
            />
            <IntegratedOrgNode
              role="Technology Lead"
              name="Kirch Ivan Balite"
              reportRole="Dev Ops Lead"
              reportName="Osiris Kedigadash Palac"
            />
            <IntegratedOrgNode role="Marketing Lead" name="Jesse Edwin Culas" />
          </div>
        </div>
      </div>

      <footer className="relative mt-12 flex flex-wrap items-center justify-between gap-2 border-t border-white/[0.07] pt-5 text-[10px] text-slate-600">
        <p>© 2026 Kirjane Labs. All rights reserved.</p>
        <p>Repository-backed beta build</p>
      </footer>
    </section>
  );
}

function IntegratedOrgNode({
  role,
  name,
  reportRole,
  reportName,
}: {
  role: string;
  name: string;
  reportRole?: string;
  reportName?: string;
}) {
  return (
    <div className="min-w-0 border-l border-white/10 pl-4">
      <p className="text-[9px] font-bold uppercase tracking-[0.16em] text-violet-300">{role}</p>
      <p className="mt-1.5 text-sm font-semibold text-slate-200">{name}</p>
      {reportRole && reportName ? (
        <div className="mt-4 border-l border-cyan-300/15 pl-3">
          <p className="text-[9px] font-bold uppercase tracking-[0.12em] text-slate-500">{reportRole}</p>
          <p className="mt-1 text-xs font-medium text-slate-400">{reportName}</p>
        </div>
      ) : null}
    </div>
  );
}

function OrgNode({
  role,
  name,
  reports,
}: {
  role: string;
  name: string;
  reports?: { role: string; name: string }[];
}) {
  return (
    <div className="flex min-w-0 flex-col items-center text-center">
      <div className="w-full rounded-xl border border-slate-200 bg-white px-2.5 py-2.5 shadow-sm">
        <p className="text-[10px] font-semibold uppercase tracking-wide text-emerald-700">{role}</p>
        <p className="mt-1 break-words text-xs font-semibold leading-snug text-navy-900">{name}</p>
      </div>
      {reports?.map((report) => (
        <div key={report.role} className="flex w-full flex-col items-center">
          <div className="h-3 w-px bg-slate-300" aria-hidden />
          <div className="w-full rounded-xl border border-slate-200 bg-white px-2.5 py-2.5 shadow-sm">
            <p className="text-[10px] font-semibold uppercase tracking-wide text-blue-700">{report.role}</p>
            <p className="mt-1 break-words text-xs font-semibold leading-snug text-navy-900">{report.name}</p>
          </div>
        </div>
      ))}
    </div>
  );
}
