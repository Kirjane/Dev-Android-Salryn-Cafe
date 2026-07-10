import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  attemptUnavailableCrystalRender,
  fetchProductionReportCatalog,
  fetchProductionReportRendererStatus,
  generateProductionReportDataset,
  type ProductionReportCatalogItem,
  type ProductionReportDatasetEnvelope,
} from "../api/productionReports";
import { Alert, Button, Card, CardBody, CardHeader, CardTitle, CommandBar, TextInput } from "../components/ui";

function defaultRange(): { start: string; end: string } {
  const end = new Date();
  const start = new Date(end.getTime() - 24 * 60 * 60 * 1000);
  return { start: start.toISOString(), end: end.toISOString() };
}

function priorityLabel(value: string | number): string {
  return typeof value === "number" ? `P${value}` : value;
}

export function ProductionReportsPage() {
  const initial = useMemo(() => defaultRange(), []);
  const [selectedKind, setSelectedKind] = useState("traditional-x-reading");
  const [rangeStart, setRangeStart] = useState(initial.start);
  const [rangeEnd, setRangeEnd] = useState(initial.end);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dataset, setDataset] = useState<ProductionReportDatasetEnvelope | null>(null);

  const catalogQuery = useQuery({ queryKey: ["production-report-catalog"], queryFn: fetchProductionReportCatalog });
  const rendererQuery = useQuery({
    queryKey: ["production-report-renderer-status"],
    queryFn: fetchProductionReportRendererStatus,
  });

  const selected: ProductionReportCatalogItem | undefined = catalogQuery.data?.find((item) => item.kind === selectedKind);

  async function generateDataset() {
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const result = await generateProductionReportDataset({
        reportKind: selectedKind,
        rangeStart,
        rangeEnd,
        registrationScopeCode: "LOCAL_SINGLE_STORE",
        documentKind: "OFFICIAL_INVOICE",
        terminalId: "HOST-UI",
      });
      setDataset(result);
      setMessage(`${result.reportTitle}: ${result.summary}`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function attemptRender() {
    if (!dataset) {
      return;
    }
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      await attemptUnavailableCrystalRender(dataset);
      setMessage("Unexpected renderer success.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="Production Reports"
        subtitle="PRE-CRYSTAL report dataset spine. Crystal runtime is unavailable until separately verified and wired."
      />

      <Alert tone="warning" title="Crystal unavailable">
        This page generates internal report datasets only. No SAP packages, .rpt files, PDF rendering, printer logic, or BIR
        approval claims are present in this milestone.
      </Alert>

      <Card>
        <CardHeader>
          <CardTitle>Renderer status</CardTitle>
        </CardHeader>
        <CardBody className="space-y-2 text-sm text-slate-700">
          {rendererQuery.isLoading ? <p>Loading renderer status…</p> : null}
          {rendererQuery.data ? (
            <pre className="overflow-auto rounded bg-slate-50 p-3 text-xs">{JSON.stringify(rendererQuery.data, null, 2)}</pre>
          ) : null}
        </CardBody>
      </Card>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(360px,520px)]">
        <Card>
          <CardHeader>
            <CardTitle>Report catalog</CardTitle>
          </CardHeader>
          <CardBody className="space-y-3">
            {catalogQuery.isLoading ? <p className="text-sm text-slate-600">Loading catalog…</p> : null}
            {catalogQuery.data?.map((item) => (
              <button
                key={item.kind}
                type="button"
                onClick={() => setSelectedKind(item.kind)}
                className={`w-full rounded-xl border p-3 text-left text-sm transition ${
                  selectedKind === item.kind ? "border-brand-500 bg-brand-50" : "border-slate-200 bg-white hover:bg-slate-50"
                }`}
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="font-medium text-slate-900">{item.title}</span>
                  <span className="rounded-full bg-slate-100 px-2 py-1 text-xs text-slate-600">{priorityLabel(item.priority)}</span>
                </div>
                <p className="mt-1 text-xs text-slate-500">{item.kind}</p>
                <p className="mt-2 text-xs text-slate-600">
                  Dataset: {item.datasetAvailable ? "available" : "unavailable"} · Permission: {item.requiredPermission}
                </p>
                {item.unavailableReason ? <p className="mt-1 text-xs text-amber-700">{item.unavailableReason}</p> : null}
              </button>
            ))}
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Generate dataset</CardTitle>
          </CardHeader>
          <CardBody className="space-y-3">
            {selected ? (
              <Alert tone={selected.datasetAvailable ? "info" : "warning"} title={selected.title}>
                {selected.datasetAvailable
                  ? "This dataset is generated through the PRE-CRYSTAL spine. Official/tax totals use canonical M5 projection where applicable."
                  : selected.unavailableReason ?? "Dataset unavailable."}
              </Alert>
            ) : null}
            <TextInput value={rangeStart} onChange={(e) => setRangeStart(e.target.value)} placeholder="Range start ISO" />
            <TextInput value={rangeEnd} onChange={(e) => setRangeEnd(e.target.value)} placeholder="Range end ISO" />
            <div className="flex flex-wrap gap-2">
              <Button disabled={busy} onClick={() => void generateDataset()}>
                Generate dataset
              </Button>
              <Button disabled={busy || !dataset} variant="secondary" onClick={() => void attemptRender()}>
                Attempt Crystal render safely
              </Button>
            </div>
            {message ? <Alert tone="success">{message}</Alert> : null}
            {error ? <Alert tone="danger">{error}</Alert> : null}
            {dataset ? (
              <div className="space-y-2">
                <p className="text-xs text-slate-600">Dataset SHA-256: {dataset.datasetSha256}</p>
                <pre className="max-h-96 overflow-auto rounded border border-slate-200 bg-white p-3 text-xs">
                  {JSON.stringify(dataset, null, 2)}
                </pre>
              </div>
            ) : null}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}
