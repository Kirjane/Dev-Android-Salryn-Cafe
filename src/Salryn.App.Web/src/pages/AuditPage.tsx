import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  downloadAuditCsv,
  fetchAuditLogs,
  fetchErrorLogs,
  type AuditLog,
  type ErrorLog,
} from "../api/audit";
import {
  Button,
  Card,
  CardBody,
  CommandBar,
  DataTable,
  ErrorState,
  LoadingState,
  TextInput,
  useNotifications,
} from "../components/ui";
import { formatDateTime } from "../utils/format";
import { safeNotificationDescription } from "../utils/notificationError";

const AUDIT_EXPORT_MAX_ROWS = 25000;

export function AuditPage() {
  const { notify } = useNotifications();
  const [tab, setTab] = useState<"audit" | "errors">("audit");
  const [search, setSearch] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);
  const [isExporting, setIsExporting] = useState(false);

  const auditQuery = useQuery({
    queryKey: ["audit-logs", search, from, to, page],
    queryFn: () =>
      fetchAuditLogs({
        search: search.trim() || undefined,
        from: from.trim() || undefined,
        to: to.trim() || undefined,
        page,
        pageSize: 25,
      }),
    enabled: tab === "audit",
  });

  const errorQuery = useQuery({
    queryKey: ["error-logs", search, from, to, page],
    queryFn: () =>
      fetchErrorLogs({
        search: search.trim() || undefined,
        from: from.trim() || undefined,
        to: to.trim() || undefined,
        page,
        pageSize: 25,
      }),
    enabled: tab === "errors",
  });

  const activeQuery = tab === "audit" ? auditQuery : errorQuery;

  useEffect(() => {
    const data = activeQuery.data;
    if (data && data.totalPages > 0 && page > data.totalPages) {
      setPage(data.totalPages);
    }
  }, [activeQuery.data, page, tab]);

  function clearFilters() {
    setPage(1);
    setSearch("");
    setFrom("");
    setTo("");
  }

  async function handleDownloadAuditCsv() {
    setIsExporting(true);
    try {
      const result = await downloadAuditCsv({
        search: search.trim() || undefined,
        from: from.trim() || undefined,
        to: to.trim() || undefined,
      });

      const file = new File([result.fileBytes], result.fileName, {
        type: "text/csv;charset=utf-8",
      });
      const url = URL.createObjectURL(file);
      try {
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = result.fileName;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
      } finally {
        URL.revokeObjectURL(url);
      }

      if (result.truncated) {
        notify.warning(
          "Audit CSV downloaded",
          `Exported ${result.exportedRowCount.toLocaleString()} of ${result.totalMatchingCount.toLocaleString()} matching records (limit ${result.maxRows.toLocaleString()}).`,
        );
      } else {
        notify.success(
          "Audit CSV downloaded",
          `Exported ${result.exportedRowCount.toLocaleString()} matching record(s) for the current filters.`,
        );
      }
    } catch (error) {
      notify.error("Audit export failed", safeNotificationDescription(error, "The CSV was not downloaded."));
    } finally {
      setIsExporting(false);
    }
  }

  const auditColumns = [
    {
      key: "when",
      header: "Date",
      render: (row: AuditLog) => formatDateTime(row.createdAt),
    },
    { key: "user", header: "User", render: (row: AuditLog) => row.userDisplayName ?? "—" },
    { key: "action", header: "Action", render: (row: AuditLog) => row.action },
    {
      key: "entity",
      header: "Entity",
      render: (row: AuditLog) => (row.entityId ? `${row.entityType} · ${row.entityId}` : row.entityType),
    },
    { key: "reason", header: "Reason", render: (row: AuditLog) => row.reason ?? "—" },
  ];

  const errorColumns = [
    {
      key: "when",
      header: "Date",
      render: (row: ErrorLog) => formatDateTime(row.createdAt),
    },
    { key: "area", header: "Area", render: (row: ErrorLog) => row.area },
    { key: "message", header: "Message", render: (row: ErrorLog) => row.message },
    { key: "user", header: "User", render: (row: ErrorLog) => row.userDisplayName ?? "—" },
  ];

  return (
    <div className="space-y-6">
      <CommandBar
        title="Audit"
        subtitle="Audit trail records committed operator and system actions (sales, catalog, stock, terminals, backups, license, settings, login) for review and investigation."
        actions={
          <div className="flex flex-wrap gap-2">
            {tab === "audit" ? (
              <Button variant="secondary" onClick={() => void handleDownloadAuditCsv()} disabled={isExporting}>
                {isExporting ? "Exporting..." : "Download audit CSV"}
              </Button>
            ) : null}
            <Button variant="secondary" onClick={() => void activeQuery.refetch()} disabled={activeQuery.isFetching}>
              {activeQuery.isFetching ? "Refreshing..." : "Refresh"}
            </Button>
          </div>
        }
      />

      <Card>
        <CardBody className="space-y-4">
          <div className="flex flex-wrap gap-2" role="tablist" aria-label="Audit and error log tabs">
            <Button
              role="tab"
              aria-selected={tab === "audit"}
              variant={tab === "audit" ? "primary" : "secondary"}
              size="sm"
              onClick={() => {
                setTab("audit");
                setPage(1);
              }}
            >
              Audit logs
            </Button>
            <Button
              role="tab"
              aria-selected={tab === "errors"}
              variant={tab === "errors" ? "primary" : "secondary"}
              size="sm"
              onClick={() => {
                setTab("errors");
                setPage(1);
              }}
            >
              Error logs
            </Button>
          </div>

          {tab === "audit" ? (
            <p className="text-sm text-slate-500">
              Download audit CSV exports the current filtered audit trail, not only the visible page. Exports up to{" "}
              {AUDIT_EXPORT_MAX_ROWS.toLocaleString()} matching records.
            </p>
          ) : (
            <p className="text-sm text-slate-500">Error log CSV export is not included in this release.</p>
          )}

          <div className="flex flex-wrap gap-3">
            <TextInput
              className="min-w-0 flex-[2_1_14rem]"
              placeholder="Search logs..."
              value={search}
              onChange={(event) => {
                setPage(1);
                setSearch(event.target.value);
              }}
            />
            <TextInput
              className="min-w-0 flex-[1_1_11rem]"
              placeholder="From ISO/date"
              value={from}
              onChange={(event) => {
                setPage(1);
                setFrom(event.target.value);
              }}
            />
            <TextInput
              className="min-w-0 flex-[1_1_11rem]"
              placeholder="To ISO/date"
              value={to}
              onChange={(event) => {
                setPage(1);
                setTo(event.target.value);
              }}
            />
            <Button className="flex-none" variant="secondary" onClick={clearFilters} disabled={!search && !from && !to}>
              Clear
            </Button>
          </div>

          {activeQuery.isLoading ? <LoadingState label="Loading logs..." /> : null}
          {activeQuery.isError ? <ErrorState message={(activeQuery.error as Error).message} /> : null}

          {tab === "audit" && auditQuery.data ? (
            <>
              <DataTable
                columns={auditColumns}
                rows={auditQuery.data.items}
                getRowKey={(row) => row.id}
                emptyTitle="No audit logs"
                emptyDescription="Audit activity will appear here when records exist."
              />
              <Pagination
                page={page}
                totalPages={auditQuery.data.totalPages}
                totalCount={auditQuery.data.totalCount}
                onPageChange={setPage}
              />
            </>
          ) : null}

          {tab === "errors" && errorQuery.data ? (
            <>
              <DataTable
                columns={errorColumns}
                rows={errorQuery.data.items}
                getRowKey={(row) => row.id}
                emptyTitle="No error logs"
                emptyDescription="Error records will appear here when they exist."
              />
              <Pagination
                page={page}
                totalPages={errorQuery.data.totalPages}
                totalCount={errorQuery.data.totalCount}
                onPageChange={setPage}
              />
            </>
          ) : null}
        </CardBody>
      </Card>
    </div>
  );
}

function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
}: {
  page: number;
  totalPages: number;
  totalCount: number;
  onPageChange: (page: number) => void;
}) {
  return (
    <div className="flex items-center justify-between text-sm text-slate-500">
      <span>
        {totalCount.toLocaleString()} records · page {page} of {Math.max(1, totalPages || 1)}
      </span>
      <div className="flex gap-2">
        <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Previous
        </Button>
        <Button variant="secondary" size="sm" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          Next
        </Button>
      </div>
    </div>
  );
}
