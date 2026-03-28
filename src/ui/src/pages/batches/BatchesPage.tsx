import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortId } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import type { BatchModel, PagedList } from '@/types';
import * as api from '@/api';

export default function BatchesPage() {
  const [data, setData] = useState<PagedList<BatchModel> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();

  const fetchData = useCallback(async () => {
    try {
      const result = await api.getBatches(page, pageSize);
      setData(result);
      setError(null);
    } catch {
      setError('Unable to load batches');
    }
  }, [page, pageSize]);

  useEffect(() => { fetchData(); }, [fetchData]);

  if (error) return <ErrorState message={error} />;
  if (!data) return <LoadingState />;

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold">Batches</h1>
        <span className="text-sm text-muted-foreground">{data.totalCount} total</span>
      </div>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[100px]">ID</TableHead>
              <TableHead>Progress</TableHead>
              <TableHead>State</TableHead>
              <TableHead>Created</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={4} className="text-center text-muted-foreground py-8">
                  No batches found
                </TableCell>
              </TableRow>
            ) : (
              data.items.map((batch) => {
                const completed = batch.totalJobs - batch.remainingJobs;
                const pct = batch.totalJobs > 0 ? Math.round((completed / batch.totalJobs) * 100) : 0;
                return (
                  <TableRow key={batch.id}>
                    <TableCell className="font-mono text-xs">
                      <Link to={`/batches/${batch.id}`} className="text-primary hover:underline">
                        {shortId(batch.id)}
                      </Link>
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center gap-2">
                        <div className="flex-1 h-2 bg-muted rounded-full overflow-hidden">
                          <div className="h-full bg-green-500 rounded-full transition-all" style={{ width: `${pct}%` }} />
                        </div>
                        <span className="text-xs text-muted-foreground w-20">
                          {completed}/{batch.totalJobs} ({pct}%)
                        </span>
                      </div>
                    </TableCell>
                    <TableCell><StateBadge state={batch.placeholderState} /></TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      <RelativeTime date={batch.createTime} />
                    </TableCell>
                  </TableRow>
                );
              })
            )}
          </TableBody>
        </Table>
      </div>

      <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={(s) => { setPageSize(s); setPage(0); }} />
    </div>
  );
}
