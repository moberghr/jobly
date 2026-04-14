import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { Pagination } from '@/components/Pagination';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import { useRefreshKey } from '@/hooks/useRefreshKey';
import type { RecurringJobModel, PagedList } from '@/types';
import * as api from '@/api';

export default function RecurringPage() {
  const [data, setData] = useState<PagedList<RecurringJobModel> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();
  const refreshKey = useRefreshKey();

  const fetchData = useCallback(async () => {
    try {
      const result = await api.getRecurringJobs(page, pageSize);
      setData(result);
      setError(null);
    } catch {
      setError('Unable to load recurring jobs');
    }
  }, [page, pageSize]);

  useEffect(() => { fetchData(); }, [fetchData, refreshKey]);

  if (error) return <ErrorState message={error} />;
  if (!data) return <LoadingState />;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Recurring Jobs</h1>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Cron</TableHead>
              <TableHead>Type</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Next Execution</TableHead>
              <TableHead>Last Execution</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={7} className="text-center text-muted-foreground py-8">
                  No recurring jobs found
                </TableCell>
              </TableRow>
            ) : (
              data.items.map((rj) => (
                <TableRow key={rj.id}>
                  <TableCell className="font-medium"><Link to={`/recurring/${rj.id}`} className="text-primary hover:underline">{rj.name}</Link></TableCell>
                  <TableCell className="font-mono text-xs">{rj.cron}</TableCell>
                  <TableCell>{rj.type.split(',')[0].split('.').pop()}</TableCell>
                  <TableCell>
                    {rj.disabledAt ? (
                      <span className="inline-flex items-center rounded-full bg-orange-100 px-2 py-0.5 text-xs font-medium text-orange-800 dark:bg-orange-900/30 dark:text-orange-400">Disabled</span>
                    ) : (
                      <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800 dark:bg-green-900/30 dark:text-green-400">Enabled</span>
                    )}
                  </TableCell>
                  <TableCell className="text-sm">
                    {rj.nextExecution ? <RelativeTime date={rj.nextExecution} /> : 'N/A'}
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {rj.lastExecution ? <RelativeTime date={rj.lastExecution} /> : 'Never'}
                  </TableCell>
                  <TableCell className="text-right">
                    {rj.disabledAt ? (
                      <Button variant="ghost" size="sm" onClick={() => { api.enableRecurringJob(rj.id).then(fetchData); }}>
                        Enable
                      </Button>
                    ) : (
                      <Button variant="ghost" size="sm" onClick={() => { api.disableRecurringJob(rj.id).then(fetchData); }}>
                        Disable
                      </Button>
                    )}
                    <Button variant="ghost" size="sm" onClick={() => { api.triggerRecurringJob(rj.id).then(fetchData); }}>
                      Trigger
                    </Button>
                    <Button variant="ghost" size="sm" className="text-destructive" onClick={() => { api.deleteRecurringJob(rj.id).then(fetchData); }}>
                      Remove
                    </Button>
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={(size) => { setPageSize(size); setPage(0); }} />
    </div>
  );
}
