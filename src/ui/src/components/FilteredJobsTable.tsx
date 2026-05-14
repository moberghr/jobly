import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortType, shortId } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { State } from '@/types';
import type { JobModel, PagedList } from '@/types';
import { JobsTableSkeleton } from '@/components/skeletons/JobsTableSkeleton';
import { useRequeueJob } from '@/api/hooks/useJobs';

const stateItems = [
  { key: 'awaiting', label: 'Awaiting', color: 'bg-orange-100 text-orange-700 dark:bg-orange-900 dark:text-orange-300' },
  { key: 'enqueued', label: 'Enqueued', color: 'bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300' },
  { key: 'processing', label: 'Processing', color: 'bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300' },
  { key: 'completed', label: 'Completed', color: 'bg-green-100 text-green-700 dark:bg-green-900 dark:text-green-300' },
  { key: 'failed', label: 'Failed', color: 'bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-300' },
];

interface FilteredJobsTableProps {
  title: string;
  parentId: string;
  parentKind: 'batch' | 'message';
  fetchJobs: (page: number, pageSize: number, state?: string) => Promise<PagedList<JobModel>>;
  fetchCounts: () => Promise<Record<string, number>>;
  onCountsUpdate?: (counts: Record<string, number>) => void;
}

export function FilteredJobsTable({ title, parentId, parentKind, fetchJobs, fetchCounts, onCountsUpdate }: FilteredJobsTableProps) {
  const [selectedState, setSelectedState] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const pageSize = 10;

  const countsQuery = useQuery({
    queryKey: [parentKind, parentId, 'jobs', 'counts'],
    queryFn: fetchCounts,
  });

  const counts = countsQuery.data ?? {};
  const countsError = countsQuery.isError;

  // Surface counts to parent and seed the initial state to the bucket with the most jobs.
  useEffect(() => {
    if (countsQuery.data) {
      onCountsUpdate?.(countsQuery.data);
    }
  }, [countsQuery.data, onCountsUpdate]);

  useEffect(() => {
    if (!countsQuery.data || selectedState !== null) {
      return;
    }
    const best = stateItems.reduce(
      (max, item) => (countsQuery.data[item.key] ?? 0) > (countsQuery.data[max.key] ?? 0) ? item : max,
      stateItems[0],
    );
    setSelectedState(best.key);
  }, [countsQuery.data, selectedState]);

  const jobsQuery = useQuery({
    queryKey: [parentKind, parentId, 'jobs', selectedState, page, pageSize],
    queryFn: () => fetchJobs(page, pageSize, selectedState ?? undefined),
    enabled: selectedState !== null,
  });

  const data = jobsQuery.data ?? null;
  const requeueJob = useRequeueJob();

  return (
    <div>
      <div className="flex items-center gap-2 mb-3">
        <h2 className="text-lg font-semibold">{title}</h2>
        {Object.keys(counts).length > 0 && (
          <span className="text-sm text-muted-foreground">({Object.values(counts).reduce((a, b) => a + b, 0)})</span>
        )}
      </div>
      {countsError && (
        <div className="text-xs text-destructive bg-destructive/10 border border-destructive/20 rounded-md px-3 py-1.5 mb-2">
          Unable to refresh counts — showing last known data
        </div>
      )}
      <div className="flex gap-4">
        {/* Vertical state sidebar */}
        <nav className="w-44 shrink-0 space-y-1">
          {stateItems.map((item) => {
            const isActive = selectedState === item.key;
            return (
              <button
                key={item.label}
                onClick={() => {
                  if (isActive) {
                    jobsQuery.refetch();
                  } else {
                    setSelectedState(item.key);
                    setPage(0);
                  }
                }}
                className={`w-full flex items-center justify-between px-3 py-2 rounded-md text-sm transition-colors text-left ${
                  isActive
                    ? 'bg-accent text-accent-foreground font-medium'
                    : 'text-muted-foreground hover:bg-accent/50'
                }`}
              >
                <span>{item.label}</span>
                <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                  (counts[item.key] ?? 0) > 0 ? item.color : 'text-muted-foreground/50'
                }`}>
                  {counts[item.key] ?? 0}
                </span>
              </button>
            );
          })}
        </nav>

        {/* Jobs table */}
        <div className="flex-1">
          {data && data.items.length > 0 ? (
            <>
              <div className="rounded-md border">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="w-[80px]">ID</TableHead>
                      <TableHead>Type</TableHead>
                      <TableHead>Handler</TableHead>
                      <TableHead className="w-[100px] text-right">State</TableHead>
                      <TableHead className="w-[120px] text-right">Created</TableHead>
                      <TableHead className="w-[80px]" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {data.items.map((job) => (
                      <TableRow key={job.id}>
                        <TableCell className="font-mono text-xs">
                          <Link to={`/detail/${job.id}`} className="text-primary hover:underline">{shortId(job.id)}</Link>
                        </TableCell>
                        <TableCell className="text-sm text-muted-foreground truncate max-w-[200px]">{shortType(job.type)}</TableCell>
                        <TableCell className="text-sm text-muted-foreground truncate max-w-[200px]">{job.handlerType ? shortType(job.handlerType) : '—'}</TableCell>
                        <TableCell className="text-right"><StateBadge state={job.currentState} /></TableCell>
                        <TableCell className="text-sm text-muted-foreground text-right"><RelativeTime date={job.createTime} /></TableCell>
                        <TableCell className="text-right">
                          {job.currentState === State.Failed && (
                            <Button variant="outline" size="sm" className="h-7 text-xs" onClick={() => requeueJob.mutate(job.id)}>
                              Requeue
                            </Button>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
              {data.pageCount > 1 && (
                <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} />
              )}
            </>
          ) : data ? (
            <div className="text-sm text-muted-foreground py-4 text-center">
              No jobs found
            </div>
          ) : (
            <JobsTableSkeleton rows={8} />
          )}
        </div>
      </div>
    </div>
  );
}
