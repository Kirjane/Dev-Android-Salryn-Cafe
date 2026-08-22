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

export type ProductionReportDatasetEnvelope = Record<string, unknown> & {
  reportKind: string;
  reportTitle: string;
  datasetSha256: string;
  summary: string;
};

export type ProductionReportRendererStatusResponse = Record<string, unknown> & {
  reportingEngine?: string;
  note?: string;
  allowedOutputKinds?: string[];
  nativePdf?: Record<string, unknown>;
  crystalCompatibility?: Record<string, unknown> & {
    disabledByDefault?: boolean;
    mode?: string;
    note?: string;
  };
};

export type ProductionReportExportRequest = ProductionReportDatasetRequest & {
  requestedOutputKind: "pdf";
};

export type ProductionReportArtifactManifest = Record<string, unknown> & {
  artifactId?: string;
  reportKind?: string;
  fileName?: string;
  contentType?: string;
  byteLength?: number;
  pdfSha256?: string;
  datasetContentSha256?: string;
  datasetSha256?: string;
  rendererName?: string;
  rendererVersion?: string;
  createdAt?: string;
  controlledRootPolicyLabel?: string;
  relativeArtifactPath?: string;
  relativeManifestPath?: string;
  exportScope?: string;
  callerPathAccepted?: boolean;
};

export type ProductionReportArtifactExportResult = Record<string, unknown> & {
  manifest?: ProductionReportArtifactManifest;
};

export async function fetchProductionReportCatalog(): Promise<ProductionReportCatalogItem[]> {
  const response = await fetch("/api/production-reports/catalog", { credentials: "include" });
  return readJson<ProductionReportCatalogItem[]>(response);
}

export async function fetchProductionReportRendererStatus(): Promise<ProductionReportRendererStatusResponse> {
  const response = await fetch("/api/production-reports/renderer/status", { credentials: "include" });
  return readJson<ProductionReportRendererStatusResponse>(response);
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

export async function exportProductionReportNativePdf(
  body: ProductionReportExportRequest,
): Promise<ProductionReportArtifactExportResult> {
  const response = await fetch("/api/production-reports/export", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return readJson<ProductionReportArtifactExportResult>(response);
}
