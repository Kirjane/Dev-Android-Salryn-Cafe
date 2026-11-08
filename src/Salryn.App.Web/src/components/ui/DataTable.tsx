import type { KeyboardEvent, ReactNode } from "react";
import { EmptyState } from "./EmptyState";

export type DataTableColumn<TRow> = {
  key: string;
  header: string;
  align?: "left" | "right" | "center";
  numeric?: boolean;
  render: (row: TRow) => ReactNode;
};

type DataTableProps<TRow> = {
  columns: DataTableColumn<TRow>[];
  rows: TRow[];
  getRowKey: (row: TRow) => string;
  emptyTitle?: string;
  emptyDescription?: string;
  onRowClick?: (row: TRow) => void;
  selectedRowKey?: string | null;
};

const alignClass = {
  left: "text-left",
  right: "text-right tabular-nums",
  center: "text-center",
} as const;

// Generic, presentation-only table primitive.
// WEB-A3: must only ever be bound to real backend data by callers. No mock rows here.
export function DataTable<TRow>({
  columns,
  rows,
  getRowKey,
  emptyTitle = "No records",
  emptyDescription = "There is nothing to show yet.",
  onRowClick,
  selectedRowKey,
}: DataTableProps<TRow>) {
  if (rows.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  function handleRowKeyDown(event: KeyboardEvent<HTMLTableRowElement>, row: TRow) {
    if (!onRowClick) return;
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      onRowClick(row);
    }
  }

  return (
    <div className="overflow-x-auto overflow-y-hidden rounded-md border border-slate-200 bg-white">
      <table className="min-w-full divide-y divide-slate-200">
        <thead className="bg-slate-100/90">
          <tr>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={`whitespace-nowrap px-3 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-slate-600 ${
                  alignClass[column.align ?? "left"]
                }`}
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100 bg-white">
          {rows.map((row) => {
            const rowKey = getRowKey(row);
            const isSelected = selectedRowKey === rowKey;
            return (
              <tr
                key={rowKey}
                tabIndex={onRowClick ? 0 : undefined}
                aria-selected={onRowClick ? isSelected : undefined}
                className={`transition-colors hover:bg-slate-50 ${
                  isSelected ? "bg-blue-50/90 shadow-[inset_3px_0_0_#2563eb]" : ""
                } ${
                  onRowClick
                    ? "cursor-pointer focus-visible:bg-blue-50/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-600"
                    : ""
                }`}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                onKeyDown={onRowClick ? (event) => handleRowKeyDown(event, row) : undefined}
              >
                {columns.map((column) => (
                  <td
                    key={column.key}
                    className={`whitespace-nowrap px-3 py-2.5 text-sm text-slate-700 ${
                      alignClass[column.align ?? "left"]
                    } ${column.numeric ? "font-mono tabular-nums" : ""}`}
                  >
                    {column.render(row)}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
