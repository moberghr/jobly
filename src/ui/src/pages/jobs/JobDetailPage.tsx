import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { shortType, formatDateTime, shortId } from '@/utils/format';
import { LoadingState, ErrorState } from '@/components/PageState';
import type { JobDetailModel } from '@/types';
import * as api from '@/api';

const logLevelColors: Record<string, string> = {
  Information: 'text-muted-foreground',
  Warning: 'text-yellow-600',
  Error: 'text-red-600',
  Critical: 'text-red-800 font-bold',
};

export default function JobDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [job, setJob] = useState<JobDetailModel | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (id) api.getJobById(id).then(setJob).catch(() => setError('Unable to load job details'));
  }, [id]);

  if (error) return <ErrorState message={error} />;
  if (!job) return <LoadingState />;

  return (
    <div className="max-w-4xl">
      <div className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">Job {shortId(job.id)}</h1>
        <StateBadge state={job.currentState} />
        <div className="flex-1" />
        <Button variant="outline" size="sm" onClick={() => api.requeueJob(job.id)}>
          Requeue
        </Button>
        <Button variant="outline" size="sm" onClick={() => api.retryJob(job.id)}>
          Retry
        </Button>
        <Button variant="destructive" size="sm" onClick={() => api.deleteJob(job.id)}>
          Delete
        </Button>
      </div>

      {/* Details + Relationships */}
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

      {/* Sibling Jobs (from same message) */}
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

      {/* Child Jobs (continuations) */}
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

      {/* State History */}
      <Card className="mb-4">
        <CardHeader className="pb-2"><CardTitle className="text-sm">State History</CardTitle></CardHeader>
        <CardContent>
          <div className="space-y-3">
            {job.stateHistory.map((s) => (
              <div key={s.id} className="flex items-start gap-3">
                <div className="w-2 h-2 mt-2 rounded-full bg-primary" />
                <div>
                  <div className="flex items-center gap-2">
                    <StateBadge state={s.state} />
                    <span className="text-xs text-muted-foreground">{formatDateTime(s.dateTime)}</span>
                  </div>
                  {s.message && <p className="text-sm text-muted-foreground mt-1">{s.message}</p>}
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      {/* Execution Logs */}
      {job.logs.length > 0 && (
        <Card className="mb-4">
          <CardHeader className="pb-2"><CardTitle className="text-sm">Execution Logs ({job.logs.length})</CardTitle></CardHeader>
          <CardContent>
            <div className="space-y-1 font-mono text-xs">
              {job.logs.map((log) => (
                <div key={log.id} className={`flex gap-2 ${logLevelColors[log.level] ?? 'text-muted-foreground'}`}>
                  <span className="text-muted-foreground shrink-0">{formatDateTime(log.timestamp)}</span>
                  <span className="shrink-0 w-20">[{log.level}]</span>
                  <span className="break-all">{log.message}</span>
                </div>
              ))}
            </div>
            {job.logs.some(l => l.exception) && (
              <div className="mt-4">
                {job.logs.filter(l => l.exception).map((log) => (
                  <pre key={log.id} className="text-xs bg-red-50 text-red-800 p-3 rounded-md overflow-auto mt-2">
                    {log.exception}
                  </pre>
                ))}
              </div>
            )}
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
