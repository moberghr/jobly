import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortType, formatRelativeTime, shortId } from '@/utils/format';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import type { JobModel, PagedList } from '@/types';
import * as api from '@/api';

const stateEndpoints: Record<string, (page: number, pageSize: number) => Promise<PagedList<JobModel>>> = {
  enqueued: api.getEnqueuedJobs,
  processing: api.getProcessingJobs,
  scheduled: api.getScheduledJobs,
  completed: api.getCompletedJobs,
  failed: api.getFailedJobs,
  awaiting: api.getAwaitingJobs,
};

export default function JobListPage() {
  const { state } = useParams<{ state: string }>();
  const [data, setData] = useState<PagedList<JobModel> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  const fetchData = useCallback(async () => {
    const fetcher = stateEndpoints[state ?? 'enqueued'];
    if (!fetcher) return;
    try {
      const result = await fetcher(page, pageSize);
      setData(result);
      setError(null);
    } catch {
      setError('Unable to load jobs');
    }
  }, [state, page, pageSize]);

  useEffect(() => {
    setPage(0);
    setSelectedIds(new Set());
  }, [state]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handlePageSizeChange = (size: number) => {
    setPageSize(size);
    setPage(0);
    setSelectedIds(new Set());
  };

  if (error) return <ErrorState message={error} />;
  if (!data) return <LoadingState />;

  const title = (state ?? 'enqueued').charAt(0).toUpperCase() + (state ?? 'enqueued').slice(1);

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold">{title} Jobs</h1>
        <span className="text-sm text-muted-foreground">{data.totalCount} total</span>
      </div>

      {selectedIds.size > 0 && (
        <div className="flex items-center gap-3 mb-3 p-3 bg-muted rounded-md">
          <span className="text-sm font-medium">{selectedIds.size} selected</span>
          <Button variant="outline" size="sm" onClick={async () => {
            await api.bulkRequeueJobs(Array.from(selectedIds));
            setSelectedIds(new Set());
            fetchData();
          }}>Requeue</Button>
          <Button variant="outline" size="sm" className="text-destructive" onClick={async () => {
            await api.bulkDeleteJobs(Array.from(selectedIds));
            setSelectedIds(new Set());
            fetchData();
          }}>Delete</Button>
          <Button variant="ghost" size="sm" onClick={() => setSelectedIds(new Set())}>Clear</Button>
        </div>
      )}

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[40px]">
                <input type="checkbox"
                  checked={data.items.length > 0 && data.items.every(j => selectedIds.has(j.id))}
                  onChange={e => {
                    if (e.target.checked) setSelectedIds(new Set(data.items.map(j => j.id)));
                    else setSelectedIds(new Set());
                  }}
                  className="rounded"
                />
              </TableHead>
              <TableHead className="w-[100px]">ID</TableHead>
              <TableHead>Type</TableHead>
              <TableHead>State</TableHead>
              <TableHead>Created</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={6} className="text-center text-muted-foreground py-8">
                  No jobs found
                </TableCell>
              </TableRow>
            ) : (
              data.items.map((job) => (
                <TableRow key={job.id}>
                  <TableCell>
                    <input type="checkbox"
                      checked={selectedIds.has(job.id)}
                      onChange={e => {
                        const next = new Set(selectedIds);
                        if (e.target.checked) next.add(job.id);
                        else next.delete(job.id);
                        setSelectedIds(next);
                      }}
                      className="rounded"
                    />
                  </TableCell>
                  <TableCell className="font-mono text-xs">
                    <Link to={`/jobs/${job.id}`} className="text-primary hover:underline">
                      {shortId(job.id)}
                    </Link>
                  </TableCell>
                  <TableCell>{shortType(job.type)}</TableCell>
                  <TableCell><StateBadge state={job.currentState} /></TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {formatRelativeTime(job.createTime)}
                  </TableCell>
                  <TableCell className="text-right">
                    <Button variant="ghost" size="sm" onClick={() => { api.requeueJob(job.id).then(fetchData); }}>
                      Requeue
                    </Button>
                    <Button variant="ghost" size="sm" className="text-destructive" onClick={() => { api.deleteJob(job.id).then(fetchData); }}>
                      Delete
                    </Button>
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={handlePageSizeChange} />
    </div>
  );
}
