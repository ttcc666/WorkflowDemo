import type { ReactNode } from "react";
import { EmptyState } from "./primitives";
import { Skeleton } from "../shadcn/skeleton";
import {
  Table,
  TableHeader,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
} from "../shadcn/table";
export interface Column<T> {
  key: string;
  label: string;
  render: (row: T) => ReactNode;
}
export function DataTable<T>({
  rows,
  columns,
  rowKey,
  loading,
  error,
  empty,
}: {
  rows: T[];
  columns: Column<T>[];
  rowKey: (row: T) => string;
  loading?: boolean;
  error?: unknown;
  empty?: string;
}) {
  return (
    <div
      className="ar-data-table approval:overflow-hidden approval:rounded-lg approval:border approval:border-border"
      aria-busy={loading || undefined}
    >
      <Table>
        <TableHeader className="approval:bg-muted/50">
          <TableRow>
            {columns.map((column) => (
              <TableHead key={column.key}>{column.label}</TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {loading && !rows.length ? (
            Array.from({ length: 3 }, (_, index) => (
              <TableRow key={index}>
                <TableCell colSpan={columns.length}>
                  <Skeleton className="approval:h-10 approval:w-full" />
                </TableCell>
              </TableRow>
            ))
          ) : rows.length ? (
            rows.map((row) => (
              <TableRow key={rowKey(row)}>
                {columns.map((column) => (
                  <TableCell
                    key={column.key}
                    data-column={column.key}
                    data-label={column.label}
                  >
                    {column.render(row)}
                  </TableCell>
                ))}
              </TableRow>
            ))
          ) : error ? (
            <TableRow>
              <TableCell colSpan={columns.length}>
                <EmptyState text="列表加载失败，请重试" />
              </TableCell>
            </TableRow>
          ) : (
            <TableRow>
              <TableCell colSpan={columns.length}>
                <EmptyState text={empty} />
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </div>
  );
}
