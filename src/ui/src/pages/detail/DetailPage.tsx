import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { FlowCard } from '@/components/FlowCard';
import { FilteredJobsTable } from '@/components/FilteredJobsTable';
import { RelativeTime } from '@/components/RelativeTime';
import { shortType, formatDateTime, shortId } from '@/utils/format';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePolling } from '@/hooks/usePolling';
import { State } from '@/types';
import type { UnifiedJobDetailModel, JobLogModel } from '@/types';
import * as api from '@/api';

const eventColors: Record<string, { border: string; bg: string; text: string }> = {
  Created:    { border: 'border-l-blue-500', bg: 'bg-blue-50 dark:bg-blue-950/30', text: 'text-blue-700 dark:text-blue-400' },
  Processing: { border: 'border-l-purple-500', bg: 'bg-purple-50 dark:bg-purple-950/30', text: 'text-purple-700 dark:text-purple-400' },
  Completed:  { border: 'border-l-green-500', bg: 'bg-green-50 dark:bg-green-950/30', text: 'text-green-700 dark:text-green-400' },
  Failed:     { border: 'border-l-red-500', bg: 'bg-red-50 dark:bg-red-950/30', text: 'text-red-700 dark:text-red-400' },
  Requeued:   { border: 'border-l-yellow-500', bg: 'bg-yellow-50 dark:bg-yellow-950/30', text: 'text-yellow-700 dark:text-yellow-400' },
  Deleted:    { border: 'border-l-gray-500', bg: 'bg-gray-50 dark:bg-gray-950/30', text: 'text-gray-700 dark:text-gray-400' },
};

function formatDuration(ms: number): string {
  if (ms < 1) return '<1ms';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  const mins = Math.floor(ms / 60000);
  const secs = ((ms % 60000) / 1000).toFixed(0);
  return `${mins}m ${secs}s`;
}

function getDuration(logs: JobLogModel[], currentIndex: number): string | null {
  const log = logs[currentIndex];
  if (log.durationMs != null) return formatDuration(log.durationMs);
  if (currentIndex >= logs.length - 1) return null;
  const current = new Date(logs[currentIndex].timestamp).getTime();
  const previous = new Date(logs[currentIndex + 1].timestamp).getTime();
  return formatDuration(current - previous);
}

function formatJson(raw: string): string {
  try { return JSON.stringify(JSON.parse(raw), null, 2); }
  catch { return raw; }
}

function kindLabel(kind: number) {
  if (kind === 3) return 'Batch';
  if (kind === 2) return 'Message';
  return 'Job';
}

export default function DetailPage() {
  const { id } = useParams<{ id: string }>();
  const [job, setJob] = useState<UnifiedJobDetailModel | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [jobCounts, setJobCounts] = useState<Record<string, number>>({});

  useEffect(() => {
    if (id) api.getDetail(id).then(setJob).catch(() => setError('Unable to load details'));
  }, [id]);

  const isProcessing = job?.currentState === State.Processing;

  usePolling(
    useCallback(() => {
      if (id && isProcessing) {
        api.getDetail(id).then(setJob).catch(() => {});
      }
    }, [id, isProcessing]),
    3000
  );

  const handleCountsUpdate = useCallback((counts: Record<string, number>) => {
    setJobCounts(counts);
  }, []);

  if (error) return <ErrorState message={error} />;
  if (!job) return <LoadingState />;

  const systemEvents = job.logs.filter(l => l.eventType !== 'Log').reverse();
  const handlerLogs = job.logs.filter(l => l.eventType === 'Log');

  // Batch progress
  const totalJobs = Object.keys(jobCounts).length > 0
    ? Object.values(jobCounts).reduce((a, b) => a + b, 0)
    : job.totalJobs;
  const completedJobs = jobCounts['completed'] ?? job.completedJobs;
  const failedJobs = jobCounts['failed'] ?? job.failedJobs;
  const done = completedJobs + failedJobs;
  const pct = totalJobs > 0 ? Math.round((done / totalJobs) * 100) : 0;
  const greenPct = totalJobs > 0 ? (completedJobs / totalJobs) * 100 : 0;
  const redPct = totalJobs > 0 ? (failedJobs / totalJobs) * 100 : 0;

  // Is this a container (batch/message) with child jobs?
  const hasChildJobs = job.kind === 2 || job.kind === 3;
  const isJob = job.kind === 1;

  const jobContext = JSON.stringify({ jobId: job.id });

  return (
    <div>
      {/* Header */}
      <div data-jobly-slot="detail.header" data-jobly-context={jobContext} key={`header-${job.id}`} className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">{kindLabel(job.kind)} {shortId(job.id)}</h1>
        <StateBadge state={job.currentState} cancellationMode={job.cancellationMode} />
        {job.queue && <span className="text-sm text-muted-foreground">Queue: {job.queue}</span>}
        <div className="flex-1" />
        {isJob && job.currentState === State.Processing ? (
          <Button variant="destructive" size="sm" onClick={() => api.deleteJob(job.id)}>Cancel</Button>
        ) : isJob ? (
          <>
            <Button variant="outline" size="sm" onClick={() => api.requeueJob(job.id)}>Requeue</Button>
            <Button variant="destructive" size="sm" onClick={() => api.deleteJob(job.id)}>Delete</Button>
          </>
        ) : null}
      </div>

      {/* Two-column layout */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Left column */}
        <div className="space-y-4">
          {/* Progress bar (batches) */}
          {totalJobs > 0 && (
            <div data-jobly-slot="detail.progress" data-jobly-context={jobContext} key={`progress-${job.id}`}>
              <Card>
                <CardHeader className="pb-2"><CardTitle className="text-sm">Progress</CardTitle></CardHeader>
                <CardContent>
                  <div className="flex items-center gap-4">
                    <div className="flex-1 h-4 bg-muted rounded-full overflow-hidden flex">
                      {greenPct > 0 && <div className="h-full bg-green-500 transition-all" style={{ width: `${greenPct}%` }} />}
                      {redPct > 0 && <div className="h-full bg-red-500 transition-all" style={{ width: `${redPct}%` }} />}
                    </div>
                    <span className="text-sm font-medium">{done}/{totalJobs} ({pct}%)</span>
                  </div>
                </CardContent>
              </Card>
            </div>
          )}

          {/* Payload & Metadata */}
          {(job.message || (job.metadata && Object.keys(job.metadata).length > 0)) && (
            <div data-jobly-slot="detail.payload" data-jobly-context={jobContext} key={`payload-${job.id}`}>
              <Card>
                <CardContent className="pt-4 space-y-4">
                  {job.message && (
                    <div>
                      <h3 className="text-sm font-semibold mb-2">Payload</h3>
                      <pre className="text-xs bg-muted p-3 rounded-md overflow-auto max-h-40">{formatJson(job.message)}</pre>
                    </div>
                  )}
                  {job.metadata && Object.keys(job.metadata).length > 0 && (
                    <div>
                      <h3 className="text-sm font-semibold mb-2">Metadata</h3>
                      <pre className="text-xs bg-muted p-3 rounded-md overflow-auto max-h-40">{JSON.stringify(job.metadata, null, 2)}</pre>
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          )}

          {/* Details */}
          <div data-jobly-slot="detail.details" data-jobly-context={jobContext} key={`details-${job.id}`}>
            <Card>
              <CardHeader className="pb-2"><CardTitle className="text-sm">Details</CardTitle></CardHeader>
              <CardContent className="space-y-2 text-sm">
                <div><span className="text-muted-foreground">Type:</span> {shortType(job.type)}</div>
                {job.handlerType && <div><span className="text-muted-foreground">Handler:</span> {shortType(job.handlerType)}</div>}
                <div><span className="text-muted-foreground">Created:</span> <RelativeTime date={job.createTime} /></div>
                {job.scheduleTime && <div><span className="text-muted-foreground">Scheduled:</span> <RelativeTime date={job.scheduleTime} /></div>}
                {job.metadata?.['ConcurrencyKey'] && <div><span className="text-muted-foreground">Mutex:</span> <span className="font-mono text-xs">{String(job.metadata['ConcurrencyKey'])}</span></div>}
                <div><span className="text-muted-foreground">ID:</span> <span className="font-mono text-xs">{job.id}</span></div>
              </CardContent>
            </Card>
          </div>

          {/* Flow */}
          <div data-jobly-slot="detail.flow" data-jobly-context={jobContext} key={`flow-${job.id}`}>
            <FlowCard
              jobId={job.id}
              traceId={job.traceId}
              parentJob={job.parentJob}
              spawnedByJob={job.spawnedByJob}
              continuations={job.continuations}
              spawnedJobs={job.spawnedJobs}
            />
          </div>
        </div>

        {/* Right column: History + Logs */}
        <div className="space-y-4">
          {/* State History */}
          {systemEvents.length > 0 && (
            <div data-jobly-slot="detail.history" data-jobly-context={jobContext} key={`history-${job.id}`}>
              <h2 className="text-sm font-semibold text-muted-foreground uppercase mb-3">History</h2>
              <div className="space-y-3">
                {systemEvents.map((event, index) => {
                  const colors = eventColors[event.eventType] ?? eventColors.Created;
                  const duration = getDuration(systemEvents, index);
                  return (
                    <div key={event.id} className={`border-l-4 ${colors.border} ${colors.bg} rounded-r-md p-4`}>
                      <div className="flex items-center justify-between">
                        <span className={`font-semibold ${colors.text}`}>{event.eventType}</span>
                        <span className="text-xs text-muted-foreground">
                          <RelativeTime date={event.timestamp} />
                          {duration && <span className="ml-2 opacity-60">({duration})</span>}
                        </span>
                      </div>
                      {event.message && <p className="text-sm text-muted-foreground mt-1">{event.message}</p>}
                      {event.exception && (
                        <pre className="text-xs bg-red-100 dark:bg-red-950/50 text-red-800 dark:text-red-300 p-3 rounded-md overflow-auto mt-2 max-h-60">
                          {event.exception}
                        </pre>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          {/* Handler Logs */}
          {handlerLogs.length > 0 && (
            <div data-jobly-slot="detail.logs" data-jobly-context={jobContext} key={`logs-${job.id}`}>
              <Card>
                <CardHeader className="pb-2"><CardTitle className="text-sm">Handler Output ({handlerLogs.length})</CardTitle></CardHeader>
                <CardContent>
                  <div className="space-y-1 font-mono text-xs max-h-[80vh] overflow-auto">
                    {handlerLogs.map((log) => (
                      <div key={log.id} className={`flex gap-2 ${
                        log.level === 'Error' ? 'text-red-600' :
                        log.level === 'Warning' ? 'text-yellow-600' :
                        'text-muted-foreground'
                      }`}>
                        <span className="text-muted-foreground shrink-0">{formatDateTime(log.timestamp)}</span>
                        <span className="shrink-0 w-20">[{log.level}]</span>
                        <span className="break-all">{log.message}</span>
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            </div>
          )}
        </div>
      </div>

      {/* Child jobs table (batches and messages) */}
      {hasChildJobs && (
        <div className="mt-6">
          <FilteredJobsTable
            key={job.id}
            title="Jobs"
            fetchJobs={(page, pageSize, state) =>
              job.kind === 3
                ? api.getBatchJobs(job.id, page, pageSize, state)
                : api.getMessageJobs(job.id, page, pageSize, state)
            }
            fetchCounts={() =>
              job.kind === 3
                ? api.getBatchJobCounts(job.id)
                : api.getMessageJobCounts(job.id)
            }
            onCountsUpdate={handleCountsUpdate}
          />
        </div>
      )}
    </div>
  );
}
