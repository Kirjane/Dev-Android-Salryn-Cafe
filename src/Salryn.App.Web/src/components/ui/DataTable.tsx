import type { ReactNode } from "react";
import { EmptyState } from "./EmptyState";

export type DataTableColumn<TRow> = {
  key: string;
  header: string;
  align?: "left" | "right" | "center";
  render: (row: TRow) => ReactNode;
};

type DataTableProps<TRow> = {
  columns: DataTableColumn<TRow>[];
  rows: TRow[];
  getRowKey: (row: TRow) => string;
  emptyTitle?: string;
  emptyDescription?: string;
};

const alignClass = {
  left: "text-left",
  right: "text-right",
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
}: DataTableProps<TRow>) {
  if (rows.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return (
    <div className="overflow-hidden rounded-xl border border-slate-200">
      <table className="min-w-full divide-y divide-slate-200">
        <thead className="bg-slate-50">
          <tr>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={`px-4 py-3 text-xs font-semibold uppercase tracking-wide text-slate-500 ${
                  alignClass[column.align ?? "left"]
                }`}
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100 bg-white">
          {rows.map((row) => (
            <tr key={getRowKey(row)} className="hover:bg-slate-50">
              {columns.map((column) => (
                <td
                  key={column.key}
                  className={`px-4 py-3 text-sm text-slate-700 ${alignClass[column.align ?? "left"]}`}
                >
                  {column.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
