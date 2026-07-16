/**
 * PRE-BETA: Salryn product identity / credits card for licensing screens.
 * Static presentation only — no network, no compliance modules, no extra products.
 */
export function ProductCreditsCard() {
  return (
    <section
      className="flex h-full flex-col overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-md shadow-slate-200/60"
      aria-label="Licensing and Credits"
    >
      <div className="border-b border-slate-100 bg-gradient-to-br from-navy-900 via-navy-800 to-blue-900 px-6 py-6 text-white">
        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-emerald-300">Kirjane Labs</p>
        <h2 className="mt-2 text-3xl font-bold tracking-tight">SALRYN</h2>
        <p className="mt-1 text-sm font-medium text-blue-100">by Kirjane Labs</p>
        <p className="mt-4 text-sm font-semibold text-white/95">{"Licensing & Credits"}</p>
      </div>

      <div className="flex flex-1 flex-col gap-5 px-6 py-5 text-sm text-slate-700">
        <dl className="grid gap-3 sm:grid-cols-1">
          <div className="rounded-lg border border-slate-100 bg-slate-50 px-3 py-2">
            <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">Publisher</dt>
            <dd className="mt-0.5 font-semibold text-navy-900">Kirjane Labs</dd>
          </div>
          <div className="rounded-lg border border-slate-100 bg-slate-50 px-3 py-2">
            <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">Product</dt>
            <dd className="mt-0.5 font-semibold text-navy-900">Salryn</dd>
          </div>
          <div className="rounded-lg border border-slate-100 bg-slate-50 px-3 py-2">
            <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">Build Type</dt>
            <dd className="mt-0.5 font-semibold text-navy-900">Repository-backed beta build</dd>
          </div>
        </dl>

        <div>
          <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-500">Products of Kirjane Labs</h3>
          <ul className="mt-2 space-y-1">
            <li className="flex items-center gap-2 font-medium text-navy-900">
              <span className="inline-block h-1.5 w-1.5 rounded-full bg-emerald-500" aria-hidden />
              Salryn
            </li>
          </ul>
        </div>

        <div className="rounded-xl border border-slate-200 bg-slate-50/80 px-3 py-4">
          <p className="text-center text-xs font-bold uppercase tracking-[0.18em] text-navy-900">Kirjane Labs</p>
          <div className="mx-auto mt-2 h-4 w-px bg-slate-300" aria-hidden />
          <div className="mx-auto h-px w-4/5 max-w-sm bg-slate-300" aria-hidden />

          <div className="mt-3 grid gap-3 sm:grid-cols-3">
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

        <div className="rounded-lg border border-blue-100 bg-blue-50/70 px-3 py-3 text-xs leading-relaxed text-slate-700">
          <p className="font-semibold text-navy-900">Support and contact</p>
          <p className="mt-1">
            For licensing, maintenance, deployment assistance, or system updates, contact Kirjane Labs / Salryn support
            at{" "}
            <span className="font-mono font-semibold text-navy-900">salryn.info@gmail.com</span>.
          </p>
        </div>

        <footer className="mt-auto border-t border-slate-100 pt-4 text-center text-[11px] leading-relaxed text-slate-500">
          <p>© 2026 Kirjane Labs. All rights reserved.</p>
          <p className="mt-1">Salryn is a local POS product designed for controlled client use.</p>
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
      <div className="w-full rounded-lg border border-slate-200 bg-white px-2 py-2 shadow-sm">
        <p className="text-[10px] font-semibold uppercase tracking-wide text-emerald-700">{role}</p>
        <p className="mt-1 break-words text-xs font-semibold leading-snug text-navy-900">{name}</p>
      </div>
      {reports?.map((report) => (
        <div key={report.role} className="flex w-full flex-col items-center">
          <div className="h-3 w-px bg-slate-300" aria-hidden />
          <div className="w-full rounded-lg border border-slate-200 bg-white px-2 py-2 shadow-sm">
            <p className="text-[10px] font-semibold uppercase tracking-wide text-blue-700">{report.role}</p>
            <p className="mt-1 break-words text-xs font-semibold leading-snug text-navy-900">{report.name}</p>
          </div>
        </div>
      ))}
    </div>
  );
}
