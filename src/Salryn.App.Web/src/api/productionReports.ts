import { readJson } from "./http";

export type ProductionReportCatalogItem = {
  kind: string;
  title: string;
  priority: string | number;
  family: string | number;
  requiredPermission: string;
  datasetAvailable: boolean;
  usesCanonicalM5Projection: boolean;
  rendererRequired: boolean;
  unavailableReason?: string | null;
  requiredFilters: string[];
};

export type ProductionReportDatasetRequest = {
  reportKind: string;
  rangeStart: string;
  rangeEnd: string;
  birMachineProfileId?: string;
  registrationScopeCode?: string;
  documentKind?: string;
  terminalId?: string;
};

export type ProductionReportRendererStatus = {
  rendererName: string;
  rendererVersion: string;
  available: boolean;
  canRenderPdf: boolean;
  canPreview: boolean;
  canPrint: boolean;
  requiresExternalRuntime: boolean;
  statusCode: string;
  message: string;
};

export type ProductionReportDatasetEnvelope = Record<string, unknown> & {
  reportKind: string;
  reportTitle: string;
  datasetSha256: string;
  summary: string;
};

export async function fetchProductionReportCatalog(): Promise<ProductionReportCatalogItem[]> {
  const response = await fetch("/api/production-reports/catalog", { credentials: "include" });
  return readJson<ProductionReportCatalogItem[]>(response);
}

export async function fetchProductionReportRendererStatus(): Promise<ProductionReportRendererStatus> {
  const response = await fetch("/api/production-reports/renderer/status", { credentials: "include" });
  return readJson<ProductionReportRendererStatus>(response);
}

export async function generateProductionReportDataset(
  body: ProductionReportDatasetRequest,
): Promise<ProductionReportDatasetEnvelope> {
  const response = await fetch("/api/production-reports/dataset", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<ProductionReportDatasetEnvelope>(response);
}

export async function attemptUnavailableCrystalRender(dataset: ProductionReportDatasetEnvelope): Promise<Record<string, unknown>> {
  const response = await fetch("/api/production-reports/render", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ dataset, requestedOutputKind: "pdf", timeoutMilliseconds: 30000 }),
  });
  return readJson<Record<string, unknown>>(response);
}
