import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { RelatedJobsTable } from '@/components/RelatedJobsTable';
import { shortType, formatDateTime, shortId } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import type { JobDetailModel, JobLogModel } from '@/types';
import * as api from '@/api';

const eventColors: Record<string, { border: string; bg: string; text: string }> = {
  Created:    { border: 'border-l-blue-500', bg: 'bg-blue-50 dark:bg-blue-950/30', text: 'text-blue-700 dark:text-blue-400' },
  Processing: { border: 'border-l-purple-500', bg: 'bg-purple-50 dark:bg-purple-950/30', text: 'text-purple-700 dark:text-purple-400' },
  Completed:  { border: 'border-l-green-500', bg: 'bg-green-50 dark:bg-green-950/30', text: 'text-green-700 dark:text-green-400' },
  Failed:     { border: 'border-l-red-500', bg: 'bg-red-50 dark:bg-red-950/30', text: 'text-red-700 dark:text-red-400' },
  Requeued:   { border: 'border-l-yellow-500', bg: 'bg-yellow-50 dark:bg-yellow-950/30', text: 'text-yellow-700 dark:text-yellow-400' },
  Deleted:    { border: 'border-l-gray-500', bg: 'bg-gray-50 dark:bg-gray-950/30', text: 'text-gray-700 dark:text-gray-400' },
};

function getDuration(logs: JobLogModel[], currentIndex: number): string | null {
  if (currentIndex >= logs.length - 1) return null;
  const current = new Date(logs[currentIndex].timestamp).getTime();
  const previous = new Date(logs[currentIndex + 1].timestamp).getTime();
  const ms = current - previous;
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  return `${(ms / 60000).toFixed(1)}m`;
}

export default function JobDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [job, setJob] = useState<JobDetailModel | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (id) api.getJobById(id).then(setJob).catch(() => setError('Unable to load job details'));
  }, [id]);

  if (error) return <ErrorState message={error} />;
  if (!job) return <LoadingState />;

  const systemEvents = job.logs.filter(l => l.eventType !== 'Log').reverse();
  const handlerLogs = job.logs.filter(l => l.eventType === 'Log');

  return (
    <div>
      {/* Header */}
      <div className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">Job {shortId(job.id)}</h1>
        <StateBadge state={job.currentState} />
        <div className="flex-1" />
        <Button variant="outline" size="sm" onClick={() => api.requeueJob(job.id)}>Requeue</Button>
        <Button variant="destructive" size="sm" onClick={() => api.deleteJob(job.id)}>Delete</Button>
      </div>

      {/* Two-column layout */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Left column: Details */}
        <div className="space-y-4">
          {/* Payload */}
          {job.message && (
            <Card>
              <CardHeader className="pb-2"><CardTitle className="text-sm">Payload</CardTitle></CardHeader>
              <CardContent>
                <pre className="text-xs bg-muted p-3 rounded-md overflow-auto max-h-40">{job.message}</pre>
              </CardContent>
            </Card>
          )}

          {/* Details */}
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Details</CardTitle></CardHeader>
            <CardContent className="space-y-2 text-sm">
              <div><span className="text-muted-foreground">Type:</span> {shortType(job.type)}</div>
              <div><span className="text-muted-foreground">Handler:</span> {job.handlerType ? shortType(job.handlerType) : 'N/A'}</div>
              <div><span className="text-muted-foreground">Created:</span> {formatDateTime(job.createTime)}</div>
              {job.scheduleTime && <div><span className="text-muted-foreground">Scheduled:</span> {formatDateTime(job.scheduleTime)}</div>}
              <div><span className="text-muted-foreground">Retries:</span> {job.retriedTimes}/{job.maxRetries}</div>
              <div><span className="text-muted-foreground">ID:</span> <span className="font-mono text-xs">{job.id}</span></div>
            </CardContent>
          </Card>

          {/* Flow */}
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Flow</CardTitle></CardHeader>
            <CardContent className="space-y-2 text-sm">
              {job.messageId && (
                <div>
                  <span className="text-muted-foreground">Spawned from Message:</span>{' '}
                  <Link to={`/messages/${job.messageId}`} className="text-primary hover:underline font-mono text-xs">{shortId(job.messageId)}</Link>
                </div>
              )}
              {job.parentJobId && (
                <div>
                  <span className="text-muted-foreground">Continuation of Job:</span>{' '}
                  <Link to={`/jobs/detail/${job.parentJobId}`} className="text-primary hover:underline font-mono text-xs">{shortId(job.parentJobId)}</Link>
                </div>
              )}
              {job.spawnedByJobId && (
                <div>
                  <span className="text-muted-foreground">Spawned by Job:</span>{' '}
                  <Link to={`/jobs/detail/${job.spawnedByJobId}`} className="text-primary hover:underline font-mono text-xs">{shortId(job.spawnedByJobId)}</Link>
                </div>
              )}
              {job.traceId && (
                <div>
                  <span className="text-muted-foreground">Trace:</span>{' '}
                  <span className="font-mono text-xs">{shortId(job.traceId)}</span>
                </div>
              )}
              {!job.messageId && !job.parentJobId && !job.spawnedByJobId && (
                <div className="text-muted-foreground">Direct job (no parent)</div>
              )}
            </CardContent>
          </Card>

          {/* Sibling Jobs */}
          <RelatedJobsTable title="Sibling Jobs" count={job.siblingJobCount} fetchJobs={(page, pageSize) => api.getSiblingJobs(job.id, page, pageSize)} />

          {/* Child Jobs */}
          <RelatedJobsTable title="Child Jobs" count={job.childJobCount} fetchJobs={(page, pageSize) => api.getChildJobs(job.id, page, pageSize)} />

          {/* Trace Jobs */}
          <RelatedJobsTable title="Trace" count={job.traceJobCount} fetchJobs={(page, pageSize) => api.getTraceJobs(job.id, page, pageSize)} />
        </div>

        {/* Right column: History + Logs */}
        <div className="space-y-4">
          {/* State History */}
          <div>
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

          {/* Handler Logs */}
          {handlerLogs.length > 0 && (
            <Card>
              <CardHeader className="pb-2"><CardTitle className="text-sm">Handler Output ({handlerLogs.length})</CardTitle></CardHeader>
              <CardContent>
                <div className="space-y-1 font-mono text-xs max-h-96 overflow-auto">
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
          )}
        </div>
      </div>
    </div>
  );
}
