import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import { shortType, formatDateTime, shortId } from '@/utils/format';
import type { RecurringJobDetailModel, RecurringJobHistoryModel, PagedList } from '@/types';
import * as api from '@/api';

export default function RecurringDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [detail, setDetail] = useState<RecurringJobDetailModel | null>(null);
  const [jobs, setJobs] = useState<PagedList<RecurringJobHistoryModel> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();

  useEffect(() => {
    if (id) {
      api.getRecurringJobById(Number(id)).then(setDetail).catch(() => setError('Unable to load recurring job'));
    }
  }, [id]);

  const fetchJobs = useCallback(async () => {
    if (id) {
      try {
        const result = await api.getRecurringJobJobs(Number(id), page, pageSize);
        setJobs(result);
      } catch {
        // Jobs loading failure is non-critical
      }
    }
  }, [id, page, pageSize]);

  useEffect(() => { fetchJobs(); }, [fetchJobs]);

  if (error) return <ErrorState message={error} />;
  if (!detail) return <LoadingState />;

  const handleToggleEnabled = async () => {
    if (detail.disabledAt) {
      await api.enableRecurringJob(detail.id);
    } else {
      await api.disableRecurringJob(detail.id);
    }
    const updated = await api.getRecurringJobById(detail.id);
    setDetail(updated);
  };

  const handleTrigger = async () => {
    await api.triggerRecurringJob(detail.id);
    const updated = await api.getRecurringJobById(detail.id);
    setDetail(updated);
    fetchJobs();
  };

  const handleDelete = async () => {
    await api.deleteRecurringJob(detail.id);
    navigate('/recurring');
  };

  return (
    <div>
      {/* Header */}
      <div className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">{detail.name}</h1>
        <span className="font-mono text-sm bg-muted px-2 py-1 rounded">{detail.cron}</span>
        {detail.disabledAt ? (
          <span className="inline-flex items-center rounded-full bg-orange-100 px-2.5 py-0.5 text-xs font-medium text-orange-800 dark:bg-orange-900/30 dark:text-orange-400">
            Disabled <RelativeTime date={detail.disabledAt} />
          </span>
        ) : (
          <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800 dark:bg-green-900/30 dark:text-green-400">Enabled</span>
        )}
        <div className="flex-1" />
        <Button variant="outline" size="sm" onClick={handleToggleEnabled}>
          {detail.disabledAt ? 'Enable' : 'Disable'}
        </Button>
        <Button variant="outline" size="sm" onClick={handleTrigger}>Trigger</Button>
        <Button variant="destructive" size="sm" onClick={handleDelete}>Delete</Button>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Left column */}
        <div className="space-y-4">
          {/* Details */}
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Details</CardTitle></CardHeader>
            <CardContent className="space-y-2 text-sm">
              <div><span className="text-muted-foreground">Type:</span> {shortType(detail.type)}</div>
              <div><span className="text-muted-foreground">Created:</span> {formatDateTime(detail.createdAt)}</div>
              {detail.updatedAt && <div><span className="text-muted-foreground">Updated:</span> {formatDateTime(detail.updatedAt)}</div>}
              <div>
                <span className="text-muted-foreground">Next Execution:</span>{' '}
                {detail.nextExecution ? <RelativeTime date={detail.nextExecution} /> : 'N/A'}
              </div>
              <div>
                <span className="text-muted-foreground">Last Execution:</span>{' '}
                {detail.lastExecution ? <RelativeTime date={detail.lastExecution} /> : 'Never'}
              </div>
              <div><span className="text-muted-foreground">ID:</span> <span className="font-mono text-xs">{detail.id}</span></div>
            </CardContent>
          </Card>

          {/* Payload */}
          {detail.message && (
            <Card>
              <CardHeader className="pb-2"><CardTitle className="text-sm">Payload</CardTitle></CardHeader>
              <CardContent>
                <pre className="text-xs bg-muted p-3 rounded-md overflow-auto max-h-40">{detail.message}</pre>
              </CardContent>
            </Card>
          )}
        </div>

        {/* Right column: Execution History */}
        <div className="space-y-4">
          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-sm">Execution History</CardTitle>
            </CardHeader>
            <CardContent>
              {jobs && jobs.items.length > 0 ? (
                <>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Job</TableHead>
                        <TableHead>State</TableHead>
                        <TableHead>Executed</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {jobs.items.map((entry, idx) => (
                        <TableRow key={entry.jobId ?? `log-${idx}`}>
                          <TableCell className="font-mono text-xs">
                            {entry.jobExists && entry.jobId ? (
                              <Link to={`/detail/${entry.jobId}`} className="text-primary hover:underline">{shortId(entry.jobId)}</Link>
                            ) : entry.jobId ? (
                              <span className="text-muted-foreground">{shortId(entry.jobId)}</span>
                            ) : (
                              <span className="text-muted-foreground">-</span>
                            )}
                          </TableCell>
                          <TableCell>
                            {entry.skipped ? (
                              <span className="inline-flex items-center rounded-full bg-orange-100 px-2 py-0.5 text-xs font-medium text-orange-800 dark:bg-orange-900/30 dark:text-orange-400">Skipped</span>
                            ) : entry.jobExists && entry.currentState != null ? (
                              <StateBadge state={entry.currentState} />
                            ) : (
                              <span className="text-xs text-muted-foreground">Cleaned up</span>
                            )}
                          </TableCell>
                          <TableCell className="text-sm"><RelativeTime date={entry.createdAt} /></TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                  <Pagination
                    page={page}
                    pageCount={jobs.pageCount}
                    onPageChange={setPage}
                    pageSize={pageSize}
                    onPageSizeChange={(size) => { setPageSize(size); setPage(0); }}
                  />
                </>
              ) : (
                <p className="text-muted-foreground text-sm py-4 text-center">No executions yet</p>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
