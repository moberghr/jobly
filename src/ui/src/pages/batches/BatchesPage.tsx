import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortId } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import { useBatchesList } from '@/api/hooks/useBatches';

export default function BatchesPage() {
  const { state } = useParams<{ state?: string }>();
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();

  useEffect(() => { setPage(0); }, [state]);

  const query = useBatchesList(state, page, pageSize);

  if (query.error) return <ErrorState message={(query.error as Error).message} />;
  if (!query.data) return <LoadingState />;

  const data = query.data;

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold">{state ? `${state.charAt(0).toUpperCase() + state.slice(1)} Batches` : 'Batches'}</h1>
        <span className="text-sm text-muted-foreground">{data.totalCount} total</span>
      </div>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[100px]">ID</TableHead>
              <TableHead>Progress</TableHead>
              <TableHead className="w-[100px] text-right">State</TableHead>
              <TableHead className="w-[120px] text-right">Created</TableHead>
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
                const done = batch.completedJobs + batch.failedJobs;
                const pct = batch.totalJobs > 0 ? Math.round((done / batch.totalJobs) * 100) : 0;
                const greenPct = batch.totalJobs > 0 ? (batch.completedJobs / batch.totalJobs) * 100 : 0;
                const redPct = batch.totalJobs > 0 ? (batch.failedJobs / batch.totalJobs) * 100 : 0;
                return (
                  <TableRow key={batch.id}>
                    <TableCell className="font-mono text-xs">
                      <Link to={`/detail/${batch.id}`} className="text-primary hover:underline">
                        {shortId(batch.id)}
                      </Link>
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center gap-2">
                        <div className="flex-1 h-2 bg-muted rounded-full overflow-hidden flex">
                          {greenPct > 0 && <div className="h-full bg-green-500 transition-all" style={{ width: `${greenPct}%` }} />}
                          {redPct > 0 && <div className="h-full bg-red-500 transition-all" style={{ width: `${redPct}%` }} />}
                        </div>
                        <span className="text-xs text-muted-foreground w-28 text-right shrink-0">
                          {done}/{batch.totalJobs} ({pct}%)
                        </span>
                      </div>
                    </TableCell>
                    <TableCell className="text-right"><StateBadge state={batch.currentState} /></TableCell>
                    <TableCell className="text-sm text-muted-foreground text-right">
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
