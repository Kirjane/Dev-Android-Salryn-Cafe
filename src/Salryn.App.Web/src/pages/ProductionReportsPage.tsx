import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Database, FileCheck2, FileText, RefreshCw, ShieldCheck } from "lucide-react";
import {
  exportProductionReportNativePdf,
  fetchProductionReportCatalog,
  fetchProductionReportRendererStatus,
  generateProductionReportDataset,
  type ProductionReportArtifactExportResult,
  type ProductionReportArtifactManifest,
  type ProductionReportCatalogItem,
  type ProductionReportDatasetEnvelope,
  type ProductionReportDatasetRequest,
} from "../api/productionReports";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  DataTable,
  LoadingState,
  StatusPill,
  TextInput,
  type DataTableColumn,
} from "../components/ui";

function defaultRange(): { start: string; end: string } {
  const end = new Date();
  const start = new Date(end.getTime() - 24 * 60 * 60 * 1000);
  return { start: start.toISOString(), end: end.toISOString() };
}

function priorityLabel(value: string | number): string {
  return typeof value === "number" ? `P${value}` : value;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

function readString(source: Record<string, unknown> | null | undefined, ...keys: string[]): string | null {
  if (!source) {
    return null;
  }

  for (const key of keys) {
    const value = source[key];
    if (typeof value === "string" && value.trim()) {
      return value;
    }
  }

  return null;
}

function readBoolean(source: Record<string, unknown> | null | undefined, ...keys: string[]): boolean | null {
  if (!source) {
    return null;
  }

  for (const key of keys) {
    const value = source[key];
    if (typeof value === "boolean") {
      return value;
    }
  }

  return null;
}

function readNumber(source: Record<string, unknown> | null | undefined, ...keys: string[]): number | null {
  if (!source) {
    return null;
  }

  for (const key of keys) {
    const value = source[key];
    if (typeof value === "number") {
      return value;
    }
  }

  return null;
}

function readManifest(result: ProductionReportArtifactExportResult | null): ProductionReportArtifactManifest | null {
  const root = asRecord(result);
  if (!root) {
    return null;
  }

  return (
    asRecord(root.manifest) ??
    asRecord(root.Manifest) ??
    asRecord(root.artifactManifest) ??
    asRecord(root.ArtifactManifest) ??
    root
  ) as ProductionReportArtifactManifest;
}

function formatBytes(value: number | null): string {
  if (value === null) {
    return "Not available";
  }

  if (value < 1024) {
    return `${value} B`;
  }

  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`;
  }

  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function safeText(value: string | number | boolean | null | undefined): string {
  if (value === null || value === undefined || value === "") {
    return "Not available";
  }

  return String(value);
}

type MetadataRow = {
  key: string;
  label: string;
  value: string;
  monospace?: boolean;
};

function buildMetadataRows(manifest: ProductionReportArtifactManifest | null): MetadataRow[] {
  const record = asRecord(manifest);
  if (!record) {
    return [];
  }

  return [
    { key: "artifact", label: "Artifact ID", value: safeText(readString(record, "artifactId", "ArtifactId")), monospace: true },
    { key: "file", label: "Safe file name", value: safeText(readString(record, "fileName", "FileName")), monospace: true },
    { key: "kind", label: "Report kind", value: safeText(readString(record, "reportKind", "ReportKind")) },
    { key: "created", label: "Created", value: safeText(readString(record, "createdAt", "CreatedAt")) },
    { key: "bytes", label: "Size", value: formatBytes(readNumber(record, "byteLength", "ByteLength")) },
    { key: "pdfhash", label: "PDF SHA-256", value: safeText(readString(record, "pdfSha256", "PdfSha256")), monospace: true },
    { key: "datasethash", label: "Dataset SHA-256", value: safeText(readString(record, "datasetSha256", "DatasetSha256")), monospace: true },
    { key: "manifest", label: "Manifest", value: safeText(readString(record, "relativeManifestPath", "RelativeManifestPath")), monospace: true },
    { key: "artifactPath", label: "Relative artifact", value: safeText(readString(record, "relativeArtifactPath", "RelativeArtifactPath")), monospace: true },
    { key: "renderer", label: "Renderer", value: safeText(readString(record, "rendererName", "RendererName")) },
    { key: "callerPath", label: "Caller path accepted", value: safeText(readBoolean(record, "callerPathAccepted", "CallerPathAccepted")) },
  ];
}

export function ProductionReportsPage() {
  const initial = useMemo(() => defaultRange(), []);
  const [selectedKind, setSelectedKind] = useState("traditional-x-reading");
  const [rangeStart, setRangeStart] = useState(initial.start);
  const [rangeEnd, setRangeEnd] = useState(initial.end);
  const [busy, setBusy] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dataset, setDataset] = useState<ProductionReportDatasetEnvelope | null>(null);
  const [exportResult, setExportResult] = useState<ProductionReportArtifactExportResult | null>(null);

  const catalogQuery = useQuery({ queryKey: ["production-report-catalog"], queryFn: fetchProductionReportCatalog });
  const rendererStatusQuery = useQuery({
    queryKey: ["production-report-renderer-status"],
    queryFn: fetchProductionReportRendererStatus,
  });
  const selected: ProductionReportCatalogItem | undefined = catalogQuery.data?.find((item) => item.kind === selectedKind);
  const exportManifest = useMemo(() => readManifest(exportResult), [exportResult]);
  const metadataRows = useMemo(() => buildMetadataRows(exportManifest), [exportManifest]);

  const body: ProductionReportDatasetRequest = {
    reportKind: selectedKind,
    rangeStart,
    rangeEnd,
    registrationScopeCode: "LOCAL_SINGLE_STORE",
    documentKind: "OFFICIAL_INVOICE",
    terminalId: "HOST-UI",
  };

  async function generateDataset() {
    setBusy(true);
    setError(null);
    setMessage(null);
    setExportResult(null);
    try {
      const result = await generateProductionReportDataset(body);
      setDataset(result);
      setMessage(`${result.reportTitle}: ${result.summary}`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function exportNativePdf() {
    setExporting(true);
    setError(null);
    setMessage(null);
    try {
      const result = await exportProductionReportNativePdf({ ...body, requestedOutputKind: "pdf" });
      const manifest = readManifest(result);
      setExportResult(result);
      setDataset(null);
      setMessage(`Native PDF export completed: ${safeText(readString(asRecord(manifest), "fileName", "FileName"))}`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setExporting(false);
    }
  }

  const catalogColumns: DataTableColumn<ProductionReportCatalogItem>[] = [
    {
      key: "title",
      header: "Report",
      render: (item) => (
        <div>
          <p className="font-semibold text-slate-900">{item.title}</p>
          <p className="mt-1 text-xs text-slate-500">{item.kind}</p>
        </div>
      ),
    },
    {
      key: "priority",
      header: "Priority",
      render: (item) => <StatusPill tone="neutral">{priorityLabel(item.priority)}</StatusPill>,
    },
    {
      key: "permission",
      header: "Guard",
      render: (item) => <span className="font-mono text-xs">{item.requiredPermission}</span>,
    },
    {
      key: "state",
      header: "Dataset",
      render: (item) => (
        <StatusPill tone={item.datasetAvailable ? "success" : "warning"}>
          {item.datasetAvailable ? "Available" : "Unavailable"}
        </StatusPill>
      ),
    },
  ];

  const metadataColumns: DataTableColumn<MetadataRow>[] = [
    { key: "label", header: "Metadata", render: (row) => row.label },
    {
      key: "value",
      header: "Value",
      render: (row) => <span className={row.monospace ? "font-mono text-xs" : ""}>{row.value}</span>,
    },
  ];

  const selectedCanRun = Boolean(selected?.datasetAvailable) && !busy && !exporting;

  return (
    <div className="space-y-6">
      <CommandBar
        title="Production Reports"
        subtitle="Native PDF export readiness for host-local operators. Controlled artifacts only; no Crystal activation, printer path, or caller-selected output directory."
        status={(
          <div className="flex flex-wrap gap-2">
            <StatusPill tone="success">Native PDF primary</StatusPill>
            <StatusPill tone="warning">Crystal parked</StatusPill>
            <StatusPill tone="neutral">No printer integration</StatusPill>
          </div>
        )}
        actions={(
          <Button variant="secondary" onClick={() => void Promise.all([catalogQuery.refetch(), rendererStatusQuery.refetch()])}>
            <RefreshCw aria-hidden="true" size={16} />
            Refresh
          </Button>
        )}
      />

      <Alert tone="warning" title="Code-side readiness boundary">
        This surface prepares local reporting/export proof. It does not enable Crystal Reports, SAP runtime, printer integration,
        arbitrary output paths, cloud upload, BIR approval, eSales production transmission, or legal production submission.
      </Alert>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <CardBody className="flex items-start gap-3">
            <FileText className="mt-1 text-blue-700" aria-hidden="true" size={22} />
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Renderer</p>
              <p className="mt-1 text-sm font-semibold text-slate-900">
                {rendererStatusQuery.data?.reportingEngine ?? "native-pdf-primary"}
              </p>
              <p className="mt-1 text-xs text-slate-500">{rendererStatusQuery.data?.note ?? "Renderer status loads from the local API."}</p>
            </div>
          </CardBody>
        </Card>
        <Card>
          <CardBody className="flex items-start gap-3">
            <ShieldCheck className="mt-1 text-green-700" aria-hidden="true" size={22} />
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Safety</p>
              <p className="mt-1 text-sm font-semibold text-slate-900">Controlled artifact root</p>
              <p className="mt-1 text-xs text-slate-500">No caller path input is exposed by this page.</p>
            </div>
          </CardBody>
        </Card>
        <Card>
          <CardBody className="flex items-start gap-3">
            <Database className="mt-1 text-slate-700" aria-hidden="true" size={22} />
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Crystal compatibility</p>
              <p className="mt-1 text-sm font-semibold text-slate-900">
                {safeText(readString(asRecord(rendererStatusQuery.data?.crystalCompatibility), "mode"))}
              </p>
              <p className="mt-1 text-xs text-slate-500">
                Disabled by default: {safeText(readBoolean(asRecord(rendererStatusQuery.data?.crystalCompatibility), "disabledByDefault"))}
              </p>
            </div>
          </CardBody>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1.1fr)_minmax(380px,520px)]">
        <Card>
          <CardHeader>
            <CardTitle>Report catalog</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            {catalogQuery.isLoading ? <LoadingState label="Loading report catalog…" /> : null}
            {catalogQuery.isError ? <Alert tone="danger">{(catalogQuery.error as Error).message}</Alert> : null}
            <DataTable
              columns={catalogColumns}
              rows={catalogQuery.data ?? []}
              getRowKey={(item) => item.kind}
              selectedRowKey={selectedKind}
              onRowClick={(item) => {
                setSelectedKind(item.kind);
                setDataset(null);
                setExportResult(null);
                setMessage(null);
                setError(null);
              }}
              emptyTitle="No report catalog"
              emptyDescription="The local API did not return any report definitions."
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Native PDF export</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            {selected ? (
              <Alert tone={selected.datasetAvailable ? "success" : "warning"} title={selected.title}>
                {selected.datasetAvailable
                  ? "This report can generate a dataset and native PDF artifact through the guarded local API."
                  : selected.unavailableReason ?? "Dataset unavailable."}
              </Alert>
            ) : (
              <Alert tone="info" title="Select a report">
                Choose a report catalog row before generating a dataset or export.
              </Alert>
            )}

            <div className="grid gap-3">
              <TextInput value={rangeStart} onChange={(e) => setRangeStart(e.target.value)} placeholder="Range start ISO" />
              <TextInput value={rangeEnd} onChange={(e) => setRangeEnd(e.target.value)} placeholder="Range end ISO" />
            </div>

            <div className="rounded-md border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600">
              <p className="font-semibold text-slate-700">Fixed safe request fields</p>
              <p className="mt-1">Registration scope: LOCAL_SINGLE_STORE · Document kind: OFFICIAL_INVOICE · Terminal: HOST-UI</p>
              <p className="mt-1">Output kind: PDF. No caller-controlled output path or filename field is sent.</p>
            </div>

            <div className="flex flex-wrap gap-2">
              <Button disabled={!selectedCanRun} onClick={() => void generateDataset()}>
                Generate dataset
              </Button>
              <Button disabled={!selectedCanRun} variant="success" onClick={() => void exportNativePdf()}>
                <FileCheck2 aria-hidden="true" size={16} />
                Export native PDF
              </Button>
            </div>

            {busy || exporting ? <LoadingState label={exporting ? "Exporting native PDF artifact…" : "Generating dataset…"} /> : null}
            {message ? <Alert tone="success">{message}</Alert> : null}
            {error ? <Alert tone="danger">{error}</Alert> : null}
          </CardBody>
        </Card>
      </div>

      {dataset ? (
        <Card>
          <CardHeader>
            <CardTitle>Dataset readiness</CardTitle>
          </CardHeader>
          <CardBody className="space-y-3">
            <div className="grid gap-3 md:grid-cols-3">
              <div className="rounded-md border border-slate-200 bg-white p-3">
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Report</p>
                <p className="mt-1 text-sm font-semibold text-slate-900">{dataset.reportTitle}</p>
              </div>
              <div className="rounded-md border border-slate-200 bg-white p-3">
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Dataset SHA-256</p>
                <p className="mt-1 break-all font-mono text-xs text-slate-700">{dataset.datasetSha256}</p>
              </div>
              <div className="rounded-md border border-slate-200 bg-white p-3">
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Summary</p>
                <p className="mt-1 text-sm text-slate-700">{dataset.summary}</p>
              </div>
            </div>
            <details className="rounded-md border border-slate-200 bg-slate-50 p-3">
              <summary className="cursor-pointer text-sm font-semibold text-slate-700">Raw dataset envelope</summary>
              <pre className="mt-3 max-h-96 overflow-auto rounded border border-slate-200 bg-white p-3 text-xs">
                {JSON.stringify(dataset, null, 2)}
              </pre>
            </details>
          </CardBody>
        </Card>
      ) : null}

      {exportManifest ? (
        <Card>
          <CardHeader>
            <CardTitle>Export artifact metadata</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <Alert tone="success" title="Native PDF artifact created">
              Metadata below is returned by the existing local API. Paths are displayed as safe file names or relative artifact labels only.
            </Alert>
            <DataTable
              columns={metadataColumns}
              rows={metadataRows}
              getRowKey={(row) => row.key}
              emptyTitle="No artifact metadata"
              emptyDescription="The API response did not include manifest fields."
            />
          </CardBody>
        </Card>
      ) : null}
    </div>
  );
}
