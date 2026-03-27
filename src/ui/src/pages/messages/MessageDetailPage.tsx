import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { StateBadge } from '@/components/StateBadge';
import { PriorityBadge } from '@/components/PriorityBadge';
import { shortType, formatDateTime, shortId } from '@/utils/format';
import type { MessageDetailModel } from '@/types';
import * as api from '@/api';

export default function MessageDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [message, setMessage] = useState<MessageDetailModel | null>(null);

  useEffect(() => {
    if (id) api.getMessageById(id).then(setMessage);
  }, [id]);

  if (!message) return <div className="text-muted-foreground">Loading...</div>;

  return (
    <div className="max-w-4xl">
      <div className="flex items-center gap-4 mb-6">
        <h1 className="text-2xl font-bold">Message {shortId(message.id)}</h1>
        <StateBadge state={message.currentState} />
        <PriorityBadge priority={message.priority} />
      </div>

      <Card className="mb-6">
        <CardHeader className="pb-2"><CardTitle className="text-sm">Details</CardTitle></CardHeader>
        <CardContent className="space-y-2 text-sm">
          <div><span className="text-muted-foreground">Type:</span> {shortType(message.type)}</div>
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

      <Card>
        <CardHeader className="pb-2"><CardTitle className="text-sm">Spawned Jobs ({message.jobs.length})</CardTitle></CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>ID</TableHead>
                <TableHead>Type</TableHead>
                <TableHead>State</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {message.jobs.map((job) => (
                <TableRow key={job.id}>
                  <TableCell className="font-mono text-xs">
                    <Link to={`/jobs/${job.id}`} className="text-primary hover:underline">
                      {shortId(job.id)}
                    </Link>
                  </TableCell>
                  <TableCell>{shortType(job.type)}</TableCell>
                  <TableCell><StateBadge state={job.currentState} /></TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {formatDateTime(job.createTime)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}
