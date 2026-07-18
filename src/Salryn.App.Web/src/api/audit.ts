import { readJson } from "./http";
import type { PagedResponse } from "./types";

export type AuditLog = {
  id: string;
  userDisplayName: string | null;
  action: string;
  entityType: string;
  entityId: string | null;
  reason: string | null;
  createdAt: string;
};

export type ErrorLog = {
  id: string;
  userDisplayName: string | null;
  area: string;
  message: string;
  createdAt: string;
};

export type LogSearchParams = {
  from?: string;
  to?: string;
  search?: string;
  page?: number;
  pageSize?: number;
};

function toQuery(params: Record<string, string | number | undefined>): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") {
      query.set(key, String(value));
    }
  }
  const text = query.toString();
  return text ? `?${text}` : "";
}

export async function fetchAuditLogs(
  params: LogSearchParams = {},
): Promise<PagedResponse<AuditLog>> {
  const response = await fetch(
    `/api/audit/logs${toQuery({
      from: params.from,
      to: params.to,
      search: params.search,
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 25,
    })}`,
    { credentials: "include" },
  );
  return readJson<PagedResponse<AuditLog>>(response);
}

export async function fetchErrorLogs(
  params: LogSearchParams = {},
): Promise<PagedResponse<ErrorLog>> {
  const response = await fetch(
    `/api/audit/errors${toQuery({
      from: params.from,
      to: params.to,
      search: params.search,
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 25,
    })}`,
    { credentials: "include" },
  );
  return readJson<PagedResponse<ErrorLog>>(response);
}

export type AuditCsvExportMeta = {
  fileName: string;
  exportedRowCount: number;
  totalMatchingCount: number;
  truncated: boolean;
  maxRows: number;
  /** Raw CSV bytes for client-side download. */
  fileBytes: ArrayBuffer;
};

/** Downloads the complete filtered audit trail (not only the visible page). */
export async function downloadAuditCsv(
  params: Pick<LogSearchParams, "from" | "to" | "search"> = {},
): Promise<AuditCsvExportMeta> {
  const response = await fetch(
    `/api/audit/logs/export${toQuery({
      from: params.from,
      to: params.to,
      search: params.search,
    })}`,
    { credentials: "include" },
  );

  if (!response.ok) {
    let message = "Audit export failed.";
    try {
      const body = (await response.json()) as { error?: string; message?: string };
      message = body.error || body.message || message;
    } catch {
      // keep default
    }
    throw new Error(message);
  }

  const fileBytes = await response.arrayBuffer();
  const disposition = response.headers.get("Content-Disposition") ?? "";
  const match = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(disposition);
  const fileName = match?.[1]?.replace(/"/g, "") || `salryn-audit-${Date.now()}.csv`;
  const exportedRowCount = Number(response.headers.get("X-Salryn-Audit-Export-Count") ?? "0");
  const totalMatchingCount = Number(response.headers.get("X-Salryn-Audit-Export-Total-Matching") ?? exportedRowCount);
  const truncated = response.headers.get("X-Salryn-Audit-Export-Truncated") === "1";
  const maxRows = Number(response.headers.get("X-Salryn-Audit-Export-Max-Rows") ?? "25000");

  return {
    fileName: decodeURIComponent(fileName),
    exportedRowCount,
    totalMatchingCount,
    truncated,
    maxRows,
    fileBytes,
  };
}
