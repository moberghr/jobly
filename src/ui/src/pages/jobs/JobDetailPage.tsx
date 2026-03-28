import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { shortType, formatDateTime, shortId, formatRelativeTime } from '@/utils/format';
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
  // Duration = time since previous event
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

  // Split logs into system events and handler logs
  const systemEvents = job.logs.filter(l => l.eventType !== 'Log').reverse(); // newest first (like Hangfire)
  const handlerLogs = job.logs.filter(l => l.eventType === 'Log');

  return (
    <div className="max-w-4xl">
      {/* Header */}
      <div className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">Job {shortId(job.id)}</h1>
        <StateBadge state={job.currentState} />
        <div className="flex-1" />
        <Button variant="outline" size="sm" onClick={() => api.requeueJob(job.id)}>
          Requeue
        </Button>
        <Button variant="destructive" size="sm" onClick={() => api.deleteJob(job.id)}>
          Delete
        </Button>
      </div>

      {/* Details + Flow */}
      <div className="grid grid-cols-2 gap-4 mb-6">
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

        <Card>
          <CardHeader className="pb-2"><CardTitle className="text-sm">Flow</CardTitle></CardHeader>
          <CardContent className="space-y-2 text-sm">
            {job.messageId && (
              <div>
                <span className="text-muted-foreground">Spawned from Message:</span>{' '}
                <Link to={`/messages/${job.messageId}`} className="text-primary hover:underline font-mono text-xs">
                  {shortId(job.messageId)}
                </Link>
              </div>
            )}
            {job.parentJobId && (
              <div>
                <span className="text-muted-foreground">Continuation of Job:</span>{' '}
                <Link to={`/jobs/${job.parentJobId}`} className="text-primary hover:underline font-mono text-xs">
                  {shortId(job.parentJobId)}
                </Link>
              </div>
            )}
            {!job.messageId && !job.parentJobId && (
              <div className="text-muted-foreground">Direct job (no parent)</div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Sibling Jobs */}
      {job.siblingJobs.length > 0 && (
        <Card className="mb-4">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">Sibling Jobs ({job.siblingJobs.length})</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>ID</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>State</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {job.siblingJobs.map((s) => (
                  <TableRow key={s.id}>
                    <TableCell className="font-mono text-xs">
                      <Link to={`/jobs/${s.id}`} className="text-primary hover:underline">{shortId(s.id)}</Link>
                    </TableCell>
                    <TableCell>{shortType(s.type)}</TableCell>
                    <TableCell><StateBadge state={s.currentState} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      {/* Child Jobs */}
      {job.childJobs.length > 0 && (
        <Card className="mb-4">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">Child Jobs ({job.childJobs.length})</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>ID</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>State</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {job.childJobs.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell className="font-mono text-xs">
                      <Link to={`/jobs/${c.id}`} className="text-primary hover:underline">{shortId(c.id)}</Link>
                    </TableCell>
                    <TableCell>{shortType(c.type)}</TableCell>
                    <TableCell><StateBadge state={c.currentState} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      {/* State History — Hangfire-style state cards, newest first */}
      <h2 className="text-sm font-semibold text-muted-foreground uppercase mb-3">History</h2>
      <div className="space-y-3 mb-6">
        {systemEvents.map((event, index) => {
          const colors = eventColors[event.eventType] ?? eventColors.Created;
          const duration = getDuration(systemEvents, index);
          return (
            <div key={event.id} className={`border-l-4 ${colors.border} ${colors.bg} rounded-r-md p-4`}>
              <div className="flex items-center justify-between">
                <span className={`font-semibold ${colors.text}`}>{event.eventType}</span>
                <span className="text-xs text-muted-foreground">
                  {formatRelativeTime(event.timestamp)}
                  {duration && <span className="ml-2 opacity-60">({duration})</span>}
                </span>
              </div>
              {event.message && (
                <p className="text-sm text-muted-foreground mt-1">{event.message}</p>
              )}
              {event.exception && (
                <pre className="text-xs bg-red-100 dark:bg-red-950/50 text-red-800 dark:text-red-300 p-3 rounded-md overflow-auto mt-2">
                  {event.exception}
                </pre>
              )}
            </div>
          );
        })}
      </div>

      {/* Handler Logs */}
      {handlerLogs.length > 0 && (
        <Card className="mb-4">
          <CardHeader className="pb-2"><CardTitle className="text-sm">Handler Output ({handlerLogs.length})</CardTitle></CardHeader>
          <CardContent>
            <div className="space-y-1 font-mono text-xs">
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

      {/* Payload */}
      {job.message && (
        <Card>
          <CardHeader className="pb-2"><CardTitle className="text-sm">Payload</CardTitle></CardHeader>
          <CardContent>
            <pre className="text-xs bg-muted p-3 rounded-md overflow-auto">{job.message}</pre>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
