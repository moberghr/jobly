import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { StateBadge } from '@/components/StateBadge';
import { FilteredJobsTable } from '@/components/FilteredJobsTable';
import { FlowCard } from '@/components/FlowCard';
import { shortId, formatDateTime } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import type { JobGroupDetailModel } from '@/types';
import * as api from '@/api';

const eventColors: Record<string, { border: string; bg: string; text: string }> = {
  Created:    { border: 'border-l-blue-500', bg: 'bg-blue-50 dark:bg-blue-950/30', text: 'text-blue-700 dark:text-blue-400' },
  Processing: { border: 'border-l-purple-500', bg: 'bg-purple-50 dark:bg-purple-950/30', text: 'text-purple-700 dark:text-purple-400' },
  Completed:  { border: 'border-l-green-500', bg: 'bg-green-50 dark:bg-green-950/30', text: 'text-green-700 dark:text-green-400' },
  Failed:     { border: 'border-l-red-500', bg: 'bg-red-50 dark:bg-red-950/30', text: 'text-red-700 dark:text-red-400' },
  Requeued:   { border: 'border-l-yellow-500', bg: 'bg-yellow-50 dark:bg-yellow-950/30', text: 'text-yellow-700 dark:text-yellow-400' },
  Deleted:    { border: 'border-l-gray-500', bg: 'bg-gray-50 dark:bg-gray-950/30', text: 'text-gray-700 dark:text-gray-400' },
};

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
  const completedJobs = jobCounts['completed'] ?? batch.completedJobs;
  const failedJobs = jobCounts['failed'] ?? batch.failedJobs;
  const done = completedJobs + failedJobs;
  const pct = totalJobs > 0 ? Math.round((done / totalJobs) * 100) : 0;
  const greenPct = totalJobs > 0 ? (completedJobs / totalJobs) * 100 : 0;
  const redPct = totalJobs > 0 ? (failedJobs / totalJobs) * 100 : 0;

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
            <div className="flex-1 h-4 bg-muted rounded-full overflow-hidden flex">
              {greenPct > 0 && <div className="h-full bg-green-500 transition-all" style={{ width: `${greenPct}%` }} />}
              {redPct > 0 && <div className="h-full bg-red-500 transition-all" style={{ width: `${redPct}%` }} />}
            </div>
            <span className="text-sm font-medium">{done}/{totalJobs} ({pct}%)</span>
          </div>
          <div className="mt-3 text-sm text-muted-foreground space-y-1">
            <div>Created: {formatDateTime(batch.createTime)}</div>
            <div>ID: <span className="font-mono text-xs">{batch.id}</span></div>
          </div>
        </CardContent>
      </Card>

      <div className="mb-6">
        <FlowCard
          parentJobId={batch.parentJobId}
          parentJobKind={batch.parentJobKind}
          traceId={batch.traceId}
          continuations={batch.continuations}
        />
      </div>

      {batch.logs.length > 0 && (
        <div className="mb-6">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase mb-3">History</h2>
          <div className="space-y-3">
            {[...batch.logs].filter(l => l.eventType !== 'Log').reverse().map((event) => {
              const colors = eventColors[event.eventType] ?? eventColors.Created;
              return (
                <div key={event.id} className={`border-l-4 ${colors.border} ${colors.bg} rounded-r-md p-4`}>
                  <div className="flex items-center justify-between">
                    <span className={`font-semibold ${colors.text}`}>{event.eventType}</span>
                    <span className="text-xs text-muted-foreground">
                      <RelativeTime date={event.timestamp} />
                    </span>
                  </div>
                  {event.message && <p className="text-sm text-muted-foreground mt-1">{event.message}</p>}
                </div>
              );
            })}
          </div>
        </div>
      )}

      <FilteredJobsTable
        title="Jobs"
        fetchJobs={(page, pageSize, state) => api.getBatchJobs(batch.id, page, pageSize, state)}
        fetchCounts={() => api.getBatchJobCounts(batch.id)}
        onCountsUpdate={handleCountsUpdate}
      />
    </div>
  );
}
