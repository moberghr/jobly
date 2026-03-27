import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortType, formatRelativeTime, shortId } from '@/utils/format';
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
  const [page, setPage] = useState(0);
  const pageSize = 20;

  const fetchData = useCallback(async () => {
    const fetcher = stateEndpoints[state ?? 'enqueued'];
    if (!fetcher) return;
    const result = await fetcher(page, pageSize);
    setData(result);
  }, [state, page]);

  useEffect(() => {
    setPage(0);
  }, [state]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  if (!data) return <div className="text-muted-foreground">Loading...</div>;

  const title = (state ?? 'enqueued').charAt(0).toUpperCase() + (state ?? 'enqueued').slice(1);

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold">{title} Jobs</h1>
        <span className="text-sm text-muted-foreground">{data.totalCount} total</span>
      </div>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
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
                <TableCell colSpan={5} className="text-center text-muted-foreground py-8">
                  No jobs found
                </TableCell>
              </TableRow>
            ) : (
              data.items.map((job) => (
                <TableRow key={job.id}>
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

      <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} />
    </div>
  );
}
