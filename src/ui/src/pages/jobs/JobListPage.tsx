import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortType, shortId } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import {
  useJobsList,
  useFailedJobsByType,
  useFailedJobTypes,
  useRequeueJob,
  useDeleteJob,
  useBulkRequeueJobs,
  useBulkDeleteJobs,
  useRequeueFailedJobsByType,
  useDeleteFailedJobsByType,
} from '@/api/hooks/useJobs';

export default function JobListPage() {
  const { state } = useParams<{ state: string }>();
  const resolvedState = state ?? 'enqueued';
  const isFailed = resolvedState === 'failed';

  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [selectedType, setSelectedType] = useState<string | null>(null);

  useEffect(() => {
    setPage(0);
    setSelectedIds(new Set());
    setSelectedType(null);
  }, [resolvedState]);

  const jobsQuery = useJobsList(resolvedState, page, pageSize);
  const filteredQuery = useFailedJobsByType(selectedType ?? '', page, pageSize, isFailed && !!selectedType);
  const typeCountsQuery = useFailedJobTypes(isFailed);

  const requeueJob = useRequeueJob();
  const deleteJob = useDeleteJob();
  const bulkRequeue = useBulkRequeueJobs();
  const bulkDelete = useBulkDeleteJobs();
  const requeueByType = useRequeueFailedJobsByType();
  const deleteByType = useDeleteFailedJobsByType();

  const activeQuery = isFailed && selectedType ? filteredQuery : jobsQuery;
  const data = activeQuery.data;
  const error = activeQuery.error;
  const typeCounts = typeCountsQuery.data ?? [];

  if (error) return <ErrorState message={(error as Error).message} />;
  if (!data) return <LoadingState />;

  const handlePageSizeChange = (size: number) => {
    setPageSize(size);
    setPage(0);
    setSelectedIds(new Set());
  };

  const title = resolvedState.charAt(0).toUpperCase() + resolvedState.slice(1);

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold">{title} Jobs</h1>
        <span className="text-sm text-muted-foreground">{data.totalCount} total</span>
      </div>

      {/* Failed type filter bar */}
      {isFailed && typeCounts.length > 0 && (
        <div className="flex flex-wrap items-center gap-2 mb-3">
          <Button
            variant={selectedType === null ? 'default' : 'outline'}
            size="sm"
            onClick={() => { setSelectedType(null); setPage(0); setSelectedIds(new Set()); }}
          >
            All
          </Button>
          {typeCounts.map(tc => (
            <Button
              key={tc.type}
              variant={selectedType === tc.type ? 'default' : 'outline'}
              size="sm"
              onClick={() => { setSelectedType(tc.type); setPage(0); setSelectedIds(new Set()); }}
            >
              {shortType(tc.type)} ({tc.count})
            </Button>
          ))}
        </div>
      )}

      {/* Bulk actions for type filter */}
      {isFailed && selectedType && (
        <div className="flex items-center gap-3 mb-3 p-3 bg-muted rounded-md">
          <span className="text-sm font-medium">Filtered: {shortType(selectedType)}</span>
          <Button variant="outline" size="sm" onClick={() => requeueByType.mutate(selectedType)}>Requeue All</Button>
          <Button variant="outline" size="sm" className="text-destructive" onClick={() => deleteByType.mutate(selectedType)}>Delete All</Button>
        </div>
      )}

      {selectedIds.size > 0 && (
        <div className="flex items-center gap-3 mb-3 p-3 bg-muted rounded-md">
          <span className="text-sm font-medium">{selectedIds.size} selected</span>
          <Button variant="outline" size="sm" onClick={() => {
            bulkRequeue.mutate(Array.from(selectedIds), { onSuccess: () => setSelectedIds(new Set()) });
          }}>Requeue</Button>
          <Button variant="outline" size="sm" className="text-destructive" onClick={() => {
            bulkDelete.mutate(Array.from(selectedIds), { onSuccess: () => setSelectedIds(new Set()) });
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
              {resolvedState === 'scheduled' && <TableHead>Scheduled</TableHead>}
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={resolvedState === 'scheduled' ? 7 : 6} className="text-center text-muted-foreground py-8">
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
                    <Link to={`/detail/${job.id}`} className="text-primary hover:underline">
                      {shortId(job.id)}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <div>{shortType(job.type)}</div>
                    {job.handlerType && <div className="text-xs text-muted-foreground">{shortType(job.handlerType)}</div>}
                  </TableCell>
                  <TableCell><StateBadge state={job.currentState} cancellationMode={job.cancellationMode} /></TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    <RelativeTime date={job.createTime} />
                  </TableCell>
                  {resolvedState === 'scheduled' && (
                    <TableCell className="text-sm text-muted-foreground">
                      <RelativeTime date={job.scheduleTime ?? job.createTime} />
                    </TableCell>
                  )}
                  <TableCell className="text-right">
                    <Button variant="ghost" size="sm" onClick={() => requeueJob.mutate(job.id)}>
                      Requeue
                    </Button>
                    <Button variant="ghost" size="sm" className="text-destructive" onClick={() => deleteJob.mutate(job.id)}>
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
