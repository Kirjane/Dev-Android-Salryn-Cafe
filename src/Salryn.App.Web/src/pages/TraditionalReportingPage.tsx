import { useMemo, useState } from "react";
import {
  exportEj,
  exportESalesDraft,
  fetchTraditionalReportingStatus,
  finalizeZReading,
  previewXReading,
} from "../api/traditionalReporting";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  CardTitle,
  CommandBar,
  TextInput,
} from "../components/ui";
import { useQuery } from "@tanstack/react-query";

function defaultRange(): { start: string; end: string } {
  const end = new Date();
  const start = new Date(end.getTime() - 24 * 60 * 60 * 1000);
  return { start: start.toISOString(), end: end.toISOString() };
}

export function TraditionalReportingPage() {
  const initial = useMemo(() => defaultRange(), []);
  const [rangeStart, setRangeStart] = useState(initial.start);
  const [rangeEnd, setRangeEnd] = useState(initial.end);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<string | null>(null);

  const statusQuery = useQuery({
    queryKey: ["traditional-reporting-status"],
    queryFn: fetchTraditionalReportingStatus,
  });

  const body = {
    rangeStart,
    rangeEnd,
    registrationScopeCode: "LOCAL_SINGLE_STORE",
    documentKind: "OFFICIAL_INVOICE",
    terminalId: "HOST-UI",
  };

  async function run(action: () => Promise<Record<string, unknown>>, label: string) {
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const result = await action();
      setMessage(`${label}: ${String(result.summary ?? "ok")}`);
      if (typeof result.utf8Content === "string") {
        setPreview(result.utf8Content.slice(0, 8000));
      } else if (result.projection) {
        setPreview(JSON.stringify(result.projection, null, 2));
      } else {
        setPreview(JSON.stringify(result, null, 2).slice(0, 8000));
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-6">
      <CommandBar
        title="Internal Compliance Reporting Lab"
        subtitle="Internal X/Z/EJ/eSales draft tools only. Not client-facing, not BIR-approved, and no external submission."
      />

      <Alert tone="warning" title="Compliance foundation only">
        This surface is hidden from standard navigation. Production activation remains OFF. eSales format is an internal
        draft while the authoritative BIR schema is unconfirmed. Export does not submit to BIR. Manual acceptance and
        compliance validation remain pending.
      </Alert>

      <Card>
        <CardHeader>
          <CardTitle>Status</CardTitle>
        </CardHeader>
        <CardBody className="space-y-2 text-sm text-slate-700">
          {statusQuery.isLoading ? <p>Loading status…</p> : null}
          {statusQuery.data ? (
            <pre className="overflow-auto rounded bg-slate-50 p-3 text-xs">
              {JSON.stringify(statusQuery.data, null, 2)}
            </pre>
          ) : null}
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Internal report range</CardTitle>
        </CardHeader>
        <CardBody className="grid gap-3 md:grid-cols-2">
          <TextInput value={rangeStart} onChange={(e) => setRangeStart(e.target.value)} placeholder="Range start ISO" />
          <TextInput value={rangeEnd} onChange={(e) => setRangeEnd(e.target.value)} placeholder="Range end ISO" />
          <div className="flex flex-wrap gap-2 md:col-span-2">
            <Button disabled={busy} onClick={() => void run(() => previewXReading(body), "X-reading preview")}>
              Preview internal X-reading
            </Button>
            <Button
              disabled={busy}
              variant="secondary"
              onClick={() => {
                if (!window.confirm("Run internal Z-reading finalize test for this range? This is not client production use.")) {
                  return;
                }
                void run(() => finalizeZReading(body), "Internal Z-reading finalize test");
              }}
            >
              Run internal Z-reading finalize test
            </Button>
            <Button disabled={busy} variant="secondary" onClick={() => void run(() => exportEj(body), "Internal EJ export")}>
              Build internal EJ export
            </Button>
            <Button
              disabled={busy}
              variant="secondary"
              onClick={() => void run(() => exportESalesDraft(body), "Internal eSales draft")}
            >
              Build internal eSales draft
            </Button>
          </div>
          {message ? <Alert tone="success">{message}</Alert> : null}
          {error ? <Alert tone="danger">{error}</Alert> : null}
          {preview ? (
            <pre className="max-h-96 overflow-auto rounded border border-slate-200 bg-white p-3 text-xs md:col-span-2">
              {preview}
            </pre>
          ) : null}
        </CardBody>
      </Card>
    </div>
  );
}
