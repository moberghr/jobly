import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { FlowCard } from '@/components/FlowCard';
import { RelativeTime } from '@/components/RelativeTime';
import { shortType } from '@/utils/format';
import type { UnifiedJobDetailModel } from '@/types';

function formatJson(raw: string): string {
  try { return JSON.stringify(JSON.parse(raw), null, 2); }
  catch { return raw; }
}

interface JobMetadataProps {
  job: UnifiedJobDetailModel;
}

export function JobMetadata({ job }: JobMetadataProps) {
  const jobContext = JSON.stringify({ jobId: job.id });
  const hasPayload = !!(job.message || (job.metadata && Object.keys(job.metadata).length > 0));

  return (
    <>
      {hasPayload && (
        <div data-warp-slot="detail.payload" data-warp-context={jobContext} key={`payload-${job.id}`}>
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

      <div data-warp-slot="detail.details" data-warp-context={jobContext} key={`details-${job.id}`}>
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

      <div data-warp-slot="detail.flow" data-warp-context={jobContext} key={`flow-${job.id}`}>
        <FlowCard
          jobId={job.id}
          traceId={job.traceId}
          parentJob={job.parentJob}
          spawnedByJob={job.spawnedByJob}
          continuations={job.continuations}
          spawnedJobs={job.spawnedJobs}
        />
      </div>
    </>
  );
}
