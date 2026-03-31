import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { StateBadge } from '@/components/StateBadge';
import { FilteredJobsTable } from '@/components/FilteredJobsTable';
import { shortType, formatDateTime, shortId } from '@/utils/format';
import { LoadingState, ErrorState } from '@/components/PageState';
import type { JobGroupDetailModel } from '@/types';
import * as api from '@/api';

export default function MessageDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [message, setMessage] = useState<JobGroupDetailModel | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (id) api.getMessageById(id).then(setMessage).catch(() => setError('Unable to load message details'));
  }, [id]);

  if (error) return <ErrorState message={error} />;
  if (!message) return <LoadingState />;

  return (
    <div>
      <div className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">Message {shortId(message.id)}</h1>
        <StateBadge state={message.currentState} />
        <span className="text-sm text-muted-foreground">Queue: {message.queue}</span>
      </div>

      <Card className="mb-6">
        <CardHeader className="pb-2"><CardTitle className="text-sm">Details</CardTitle></CardHeader>
        <CardContent className="space-y-2 text-sm">
          <div><span className="text-muted-foreground">Type:</span> {message.type ? shortType(message.type) : '—'}</div>
          <div><span className="text-muted-foreground">Created:</span> {formatDateTime(message.createTime)}</div>
          <div><span className="text-muted-foreground">Jobs remaining:</span> {message.jobCount}</div>
          <div><span className="text-muted-foreground">ID:</span> <span className="font-mono text-xs">{message.id}</span></div>
        </CardContent>
      </Card>

      <Card className="mb-6">
        <CardHeader className="pb-2"><CardTitle className="text-sm">Payload</CardTitle></CardHeader>
        <CardContent>
          <pre className="text-xs bg-muted p-3 rounded-md overflow-auto">{message.payload}</pre>
        </CardContent>
      </Card>

      <FilteredJobsTable
        title="Spawned Jobs"
        fetchJobs={(page, pageSize, state) => api.getMessageJobs(message.id, page, pageSize, state)}
        fetchCounts={() => api.getMessageJobCounts(message.id)}
      />
    </div>
  );
}
