import { Button } from '@/components/ui/button';
import { StateBadge } from '@/components/StateBadge';
import { shortId } from '@/utils/format';
import { State } from '@/types';
import type { UnifiedJobDetailModel } from '@/types';
import * as api from '@/api';

function kindLabel(kind: number) {
  if (kind === 3) return 'Batch';
  if (kind === 2) return 'Message';
  return 'Job';
}

interface JobHeaderProps {
  job: UnifiedJobDetailModel;
}

export function JobHeader({ job }: JobHeaderProps) {
  const isJob = job.kind === 1;
  const jobContext = JSON.stringify({ jobId: job.id });

  return (
    <div
      data-warp-slot="detail.header"
      data-warp-context={jobContext}
      key={`header-${job.id}`}
      className="flex items-center gap-4 mb-6"
    >
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
  );
}
