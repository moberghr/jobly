import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

interface TableSkeletonProps {
  rows?: number;
  columns?: number;
  headers?: string[];
}

export function TableSkeleton({ rows = 8, columns, headers }: TableSkeletonProps) {
  const colCount = headers?.length ?? columns ?? 5;

  return (
    <div className="rounded-md border overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            {Array.from({ length: colCount }).map((_, i) => (
              <TableHead key={i}>{headers?.[i] ?? ''}</TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: rows }).map((_, r) => (
            <TableRow key={r}>
              {Array.from({ length: colCount }).map((__, c) => (
                <TableCell key={c}>
                  <Skeleton className="h-4 w-full max-w-[140px]" />
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
