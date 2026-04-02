import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { StateBadge } from '@/components/StateBadge';
import { FilteredJobsTable } from '@/components/FilteredJobsTable';
import { shortId, shortType, formatDateTime } from '@/utils/format';
import { LoadingState, ErrorState } from '@/components/PageState';
import type { JobGroupDetailModel } from '@/types';
import * as api from '@/api';

export default function BatchDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [batch, setBatch] = useState<JobGroupDetailModel | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [jobCounts, setJobCounts] = useState<Record<string, number>>({});

  useEffect(() => {
    if (id) api.getBatchById(id).then(setBatch).catch(() => setError('Unable to load batch'));
  }, [id]);

  const handleCountsUpdate = useCallback((counts: Record<string, number>) => {
    setJobCounts(counts);
  }, []);

  if (error) return <ErrorState message={error} />;
  if (!batch) return <LoadingState />;

  const totalJobs = Object.keys(jobCounts).length > 0
    ? Object.values(jobCounts).reduce((a, b) => a + b, 0)
    : batch.totalJobs;
  const completedJobs = jobCounts['completed'] ?? (batch.totalJobs - batch.jobCount);
  const pct = totalJobs > 0 ? Math.round((completedJobs / totalJobs) * 100) : 0;

  return (
    <div>
      <div className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">Batch {shortId(batch.id)}</h1>
        <StateBadge state={batch.currentState} />
      </div>

      <Card className="mb-6">
        <CardHeader className="pb-2"><CardTitle className="text-sm">Progress</CardTitle></CardHeader>
        <CardContent>
          <div className="flex items-center gap-4">
            <div className="flex-1 h-4 bg-muted rounded-full overflow-hidden">
              <div className="h-full bg-green-500 rounded-full transition-all" style={{ width: `${pct}%` }} />
            </div>
            <span className="text-sm font-medium">{completedJobs}/{totalJobs} ({pct}%)</span>
          </div>
          <div className="mt-3 text-sm text-muted-foreground space-y-1">
            <div>Created: {formatDateTime(batch.createTime)}</div>
            {batch.parentJobId && (
              <div>Parent: <Link to={batch.parentJobKind === 3 ? `/batches/detail/${batch.parentJobId}` : `/jobs/detail/${batch.parentJobId}`} className="text-primary hover:underline font-mono text-xs">{shortId(batch.parentJobId)}</Link></div>
            )}
            <div>ID: <span className="font-mono text-xs">{batch.id}</span></div>
            {batch.continuations.length > 0 && (
              <div>
                Continuations:{' '}
                {batch.continuations.map((c, i) => (
                  <span key={c.id} className="inline-flex items-center gap-1">
                    {i > 0 && ', '}
                    <Link to={`/batches/detail/${c.id}`} className="text-primary hover:underline font-mono text-xs">
                      {shortId(c.id)}
                    </Link>
                    {c.handlerType && <span className="text-xs text-muted-foreground">({shortType(c.handlerType)})</span>}
                    <StateBadge state={c.currentState} />
                  </span>
                ))}
              </div>
            )}
          </div>
        </CardContent>
      </Card>

      <FilteredJobsTable
        title="Jobs"
        fetchJobs={(page, pageSize, state) => api.getBatchJobs(batch.id, page, pageSize, state)}
        fetchCounts={() => api.getBatchJobCounts(batch.id)}
        onCountsUpdate={handleCountsUpdate}
      />
    </div>
  );
}
