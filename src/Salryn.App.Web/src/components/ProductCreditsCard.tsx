/**
 * PRE-BETA: clean Salryn credits / product identity card for licensing screens.
 * Credits-first — no admin-style metadata boxes.
 */
export function ProductCreditsCard() {
  return (
    <section
      className="flex h-full flex-col overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-md shadow-slate-200/60"
      aria-label="Licensing and Credits"
    >
      <div className="border-b border-slate-100 bg-gradient-to-br from-navy-900 via-navy-800 to-blue-900 px-6 py-7 text-white">
        <h2 className="text-3xl font-bold tracking-tight">SALRYN</h2>
        <p className="mt-1 text-sm font-medium text-blue-100">by Kirjane Labs</p>
        <p className="mt-4 text-sm font-semibold tracking-wide text-emerald-200">{"Licensing & Credits"}</p>
        <p className="mt-3 text-xs font-medium text-white/75">A Kirjane Labs product</p>
      </div>

      <div className="flex flex-1 flex-col gap-5 px-5 py-5 sm:px-6">
        <div>
          <h3 className="text-center text-xs font-bold uppercase tracking-[0.2em] text-navy-900">Credits</h3>
          <p className="mt-1 text-center text-[11px] text-slate-500">Kirjane Labs</p>
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
