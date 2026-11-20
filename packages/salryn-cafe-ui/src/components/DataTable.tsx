import type { ReactNode } from "react";
import { EmptyState, LoadingState } from "./Feedback";

export type DataTableColumn<T> = {
  key: string;
  header: string;
  align?: "left" | "right" | "center";
  tabular?: boolean;
  render: (row: T) => ReactNode;
};

export type DataTableProps<T> = {
  caption: string;
  columns: DataTableColumn<T>[];
  rows: T[];
  getRowKey: (row: T) => string;
  loading?: boolean;
  emptyTitle?: string;
  emptyDescription?: string;
};

export function DataTable<T>({
  caption,
  columns,
  rows,
  getRowKey,
  loading = false,
  emptyTitle = "No records found",
  emptyDescription = "Records will appear here once created.",
}: DataTableProps<T>) {
  if (loading) {
    return <LoadingState label={`Loading ${caption.toLowerCase()}...`} />;
  }

  if (rows.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return (
    <div className="salryn-table-wrap" role="region" aria-label={caption} tabIndex={0}>
      <table className="salryn-table">
        <caption className="salryn-sr-only">{caption}</caption>
        <thead>
          <tr>
            {columns.map((col) => (
              <th
                key={col.key}
                scope="col"
                style={{ textAlign: col.align ?? "left" }}
              >
                {col.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={getRowKey(row)}>
              {columns.map((col) => (
                <td
                  key={col.key}
                  className={col.tabular ? "salryn-tabular" : undefined}
                  style={{ textAlign: col.align ?? "left" }}
                >
                  {col.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
