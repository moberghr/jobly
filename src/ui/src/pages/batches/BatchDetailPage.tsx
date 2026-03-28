import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { StateBadge } from '@/components/StateBadge';
import { shortType, shortId, formatDateTime } from '@/utils/format';
import { LoadingState, ErrorState } from '@/components/PageState';
import type { BatchDetailModel } from '@/types';
import * as api from '@/api';

export default function BatchDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [batch, setBatch] = useState<BatchDetailModel | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (id) api.getBatchById(id).then(setBatch).catch(() => setError('Unable to load batch'));
  }, [id]);

  if (error) return <ErrorState message={error} />;
  if (!batch) return <LoadingState />;

  const completed = batch.totalJobs - batch.remainingJobs;
  const pct = batch.totalJobs > 0 ? Math.round((completed / batch.totalJobs) * 100) : 0;

  return (
    <div>
      <div className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">Batch {shortId(batch.id)}</h1>
        <StateBadge state={batch.placeholderState} />
      </div>

      <Card className="mb-6">
        <CardHeader className="pb-2"><CardTitle className="text-sm">Progress</CardTitle></CardHeader>
        <CardContent>
          <div className="flex items-center gap-4">
            <div className="flex-1 h-4 bg-muted rounded-full overflow-hidden">
              <div className="h-full bg-green-500 rounded-full transition-all" style={{ width: `${pct}%` }} />
            </div>
            <span className="text-sm font-medium">{completed}/{batch.totalJobs} ({pct}%)</span>
          </div>
          <div className="mt-3 text-sm text-muted-foreground space-y-1">
            <div>Created: {formatDateTime(batch.createTime)}</div>
            <div>ID: <span className="font-mono text-xs">{batch.id}</span></div>
            {batch.continuationJobId && (
              <div>
                Continuation: <Link to={`/jobs/detail/${batch.continuationJobId}`} className="text-primary hover:underline font-mono text-xs">{shortId(batch.continuationJobId)}</Link>
              </div>
            )}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-2"><CardTitle className="text-sm">Jobs ({batch.jobs.length})</CardTitle></CardHeader>
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
              {batch.jobs.map((job) => (
                <TableRow key={job.id}>
                  <TableCell className="font-mono text-xs">
                    <Link to={`/jobs/detail/${job.id}`} className="text-primary hover:underline">{shortId(job.id)}</Link>
                  </TableCell>
                  <TableCell>{shortType(job.type)}</TableCell>
                  <TableCell><StateBadge state={job.currentState} /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}
