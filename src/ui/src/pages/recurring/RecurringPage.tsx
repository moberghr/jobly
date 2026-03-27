import { useState, useEffect, useCallback } from 'react';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { Pagination } from '@/components/Pagination';
import { formatRelativeTime } from '@/utils/format';
import { LoadingState, ErrorState } from '@/components/PageState';
import type { RecurringJobModel, PagedList } from '@/types';
import * as api from '@/api';

export default function RecurringPage() {
  const [data, setData] = useState<PagedList<RecurringJobModel> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(0);

  const fetchData = useCallback(async () => {
    try {
      const result = await api.getRecurringJobs(page, 20);
      setData(result);
      setError(null);
    } catch {
      setError('Unable to load recurring jobs');
    }
  }, [page]);

  useEffect(() => { fetchData(); }, [fetchData]);

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
              <TableHead>Next Execution</TableHead>
              <TableHead>Last Execution</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={6} className="text-center text-muted-foreground py-8">
                  No recurring jobs found
                </TableCell>
              </TableRow>
            ) : (
              data.items.map((rj) => (
                <TableRow key={rj.id}>
                  <TableCell className="font-medium">{rj.name}</TableCell>
                  <TableCell className="font-mono text-xs">{rj.cron}</TableCell>
                  <TableCell>{rj.type.split(',')[0].split('.').pop()}</TableCell>
                  <TableCell className="text-sm">
                    {rj.nextExecution ? formatRelativeTime(rj.nextExecution) : 'N/A'}
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {rj.lastExecution ? formatRelativeTime(rj.lastExecution) : 'Never'}
                  </TableCell>
                  <TableCell className="text-right">
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

      <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} />
    </div>
  );
}
