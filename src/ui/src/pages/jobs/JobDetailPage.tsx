import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { shortType, formatDateTime, shortId } from '@/utils/format';
import type { JobDetailModel } from '@/types';
import * as api from '@/api';

export default function JobDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [job, setJob] = useState<JobDetailModel | null>(null);

  useEffect(() => {
    if (id) api.getJobById(id).then(setJob);
  }, [id]);

  if (!job) return <div className="text-muted-foreground">Loading...</div>;

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
          <CardHeader className="pb-2"><CardTitle className="text-sm">Relationships</CardTitle></CardHeader>
          <CardContent className="space-y-2 text-sm">
            {job.messageId && (
              <div>
                <span className="text-muted-foreground">Message:</span>{' '}
                <Link to={`/messages/${job.messageId}`} className="text-primary hover:underline font-mono text-xs">
                  {shortId(job.messageId)}
                </Link>
              </div>
            )}
            {job.parentJobId && (
              <div>
                <span className="text-muted-foreground">Parent Job:</span>{' '}
                <Link to={`/jobs/${job.parentJobId}`} className="text-primary hover:underline font-mono text-xs">
                  {shortId(job.parentJobId)}
                </Link>
              </div>
            )}
            {!job.messageId && !job.parentJobId && (
              <div className="text-muted-foreground">No relationships</div>
            )}
          </CardContent>
        </Card>
      </div>

      <Card>
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

      {job.message && (
        <Card className="mt-4">
          <CardHeader className="pb-2"><CardTitle className="text-sm">Payload</CardTitle></CardHeader>
          <CardContent>
            <pre className="text-xs bg-muted p-3 rounded-md overflow-auto">{job.message}</pre>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
