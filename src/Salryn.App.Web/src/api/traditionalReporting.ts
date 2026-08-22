import { readJson } from "./http";

export type ReportingRangeRequest = {
  rangeStart: string;
  rangeEnd: string;
  birMachineProfileId?: string;
  registrationScopeCode?: string;
  documentKind?: string;
  terminalId?: string;
};

export async function fetchTraditionalReportingStatus(): Promise<Record<string, unknown>> {
  const response = await fetch("/api/official/traditional-reporting/status", { credentials: "include" });
  return readJson<Record<string, unknown>>(response);
}

export async function previewXReading(body: ReportingRangeRequest): Promise<Record<string, unknown>> {
  const response = await fetch("/api/official/traditional-reporting/x-reading", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<Record<string, unknown>>(response);
}

export async function finalizeZReading(body: ReportingRangeRequest): Promise<Record<string, unknown>> {
  const response = await fetch("/api/official/traditional-reporting/z-reading/finalize", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<Record<string, unknown>>(response);
}

export async function exportEj(body: ReportingRangeRequest): Promise<Record<string, unknown>> {
  const response = await fetch("/api/official/traditional-reporting/ej-export", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<Record<string, unknown>>(response);
}

export async function exportESalesDraft(body: ReportingRangeRequest): Promise<Record<string, unknown>> {
  const response = await fetch("/api/official/traditional-reporting/esales-draft", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<Record<string, unknown>>(response);
}
