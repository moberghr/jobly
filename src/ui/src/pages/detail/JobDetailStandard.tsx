import { Repeat, Trash2, X } from 'lucide-react';
import { Panel, PanelHeader, Eyebrow } from '@/components/v2/Panel';
import { StateBadge } from '@/components/StateBadge';
import { shortId, shortType } from '@/utils/format';
import { State } from '@/types';
import type { UnifiedJobDetailModel, JobLogModel } from '@/types';
import { useDeleteJob, useRequeueJob } from '@/api/hooks/useJobs';
import { JobTimeline } from './JobTimeline';
import { JobProgress } from './JobProgress';
import { JobLogs } from './JobLogs';
import { RelatedJobsSection } from './RelatedJobsSection';

function kindLabel(kind: number) {
  if (kind === 3) {
    return 'Batch';
  }
  if (kind === 2) {
    return 'Message';
  }

  return 'Job';
}

function formatJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

interface JobDetailStandardProps {
  job: UnifiedJobDetailModel;
  systemEvents: JobLogModel[];
  handlerLogs: JobLogModel[];
  reportedBars: Array<[string, number]>;
  jobCounts: Record<string, number>;
  onCountsUpdate: (counts: Record<string, number>) => void;
}

export function JobDetailStandard({
  job,
  systemEvents,
  handlerLogs,
  reportedBars,
  jobCounts,
  onCountsUpdate,
}: JobDetailStandardProps) {
  const requeue = useRequeueJob();
  const deleteJob = useDeleteJob();

  const isJob = job.kind === 1;
  const isProcessing = job.currentState === State.Processing;
  const hasChildJobs = job.kind === 2 || job.kind === 3;

  const totalJobs =
    Object.keys(jobCounts).length > 0 ? Object.values(jobCounts).reduce((a, b) => a + b, 0) : job.totalJobs;
  const completedJobs = jobCounts['completed'] ?? job.completedJobs;
  const failedJobs = jobCounts['failed'] ?? job.failedJobs;

  const accent =
    job.currentState === State.Completed
      ? 'var(--warp-green)'
      : job.currentState === State.Failed
        ? 'var(--warp-red)'
        : job.currentState === State.Processing
          ? 'var(--warp-purple)'
          : 'var(--warp-blue)';

  return (
    <div className="space-y-3.5">
      {/* HEADER */}
      <Panel accent={accent}>
        <div className="flex flex-col gap-4 px-5 py-4 lg:flex-row lg:items-center">
          <div className="min-w-0 flex-1">
            <Eyebrow color={accent}>{kindLabel(job.kind)} detail</Eyebrow>
            <div className="mt-1 flex flex-wrap items-center gap-3">
              <h1 className="font-display text-[28px] font-semibold leading-tight tracking-tight text-foreground">
                {kindLabel(job.kind)} <span className="mono">{shortId(job.id)}</span>
              </h1>
              <StateBadge state={job.currentState} cancellationMode={job.cancellationMode} />
              {job.type && <span className="text-sm text-text-dim">{shortType(job.type)}</span>}
            </div>
            <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-text-dim">
              <span>
                <span className="mono text-[10.5px] uppercase tracking-[0.06em] text-text-mute">Created</span>{' '}
                <span className="mono">{new Date(job.createTime).toLocaleString()}</span>
              </span>
              {job.queue && (
                <span>
                  <span className="mono text-[10.5px] uppercase tracking-[0.06em] text-text-mute">Queue</span>{' '}
                  <span className="mono">{job.queue}</span>
                </span>
              )}
              <span>
                <span className="mono text-[10.5px] uppercase tracking-[0.06em] text-text-mute">ID</span>{' '}
                <span className="mono text-[11px]">{job.id}</span>
              </span>
            </div>
          </div>
          {isJob && (
            <div className="flex flex-wrap items-center gap-2">
              {isProcessing ? (
                <button
                  onClick={() => deleteJob.mutate(job.id)}
                  disabled={deleteJob.isPending}
                  className="inline-flex items-center gap-1.5 rounded-md border border-warp-red bg-warp-red-soft px-3 py-1.5 text-xs font-medium text-warp-red disabled:opacity-60"
                >
                  <X size={13} /> Cancel
                </button>
              ) : (
                <>
                  <button
                    onClick={() => requeue.mutate(job.id)}
                    disabled={requeue.isPending}
                    className="inline-flex items-center gap-1.5 rounded-md border border-border bg-panel-2 px-3 py-1.5 text-xs font-medium text-foreground hover:brightness-110 disabled:opacity-60"
                  >
                    <Repeat size={13} /> Requeue
                  </button>
                  <button
                    onClick={() => deleteJob.mutate(job.id)}
                    disabled={deleteJob.isPending}
                    className="inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs font-medium text-warp-red disabled:opacity-60"
                    style={{ borderColor: 'rgba(220,38,38,0.35)' }}
                  >
                    <Trash2 size={13} /> Delete
                  </button>
                </>
              )}
            </div>
          )}
        </div>
      </Panel>

      {/* BODY GRID */}
      <div className="grid grid-cols-1 gap-3.5 lg:grid-cols-2">
        <div className="flex flex-col gap-3.5">
          {job.message && (
            <Panel>
              <PanelHeader eyebrow="Payload" />
              <pre className="mono m-0 max-h-[60vh] overflow-auto bg-[color:var(--panel-2)] px-4 py-3 text-[11.5px] leading-[1.7] text-text-dim">
                {formatJson(job.message)}
              </pre>
            </Panel>
          )}

          {job.metadata && Object.keys(job.metadata).length > 0 && (
            <Panel>
              <PanelHeader eyebrow="Metadata" />
              <pre className="mono m-0 max-h-60 overflow-auto bg-[color:var(--panel-2)] px-4 py-3 text-[11.5px] leading-[1.7] text-text-dim">
                {JSON.stringify(job.metadata, null, 2)}
              </pre>
            </Panel>
          )}

          <Panel>
            <PanelHeader eyebrow="Details" />
            <div className="space-y-2 px-4 py-3 text-sm">
              {job.type && (
                <DetailRow k="Type" v={<span>{shortType(job.type)}</span>} />
              )}
              {job.handlerType && (
                <DetailRow k="Handler" v={<span className="mono text-xs">{shortType(job.handlerType)}</span>} />
              )}
              <DetailRow k="Created" v={<span className="mono text-xs">{new Date(job.createTime).toLocaleString()}</span>} />
              {job.scheduleTime && (
                <DetailRow k="Scheduled" v={<span className="mono text-xs">{new Date(job.scheduleTime).toLocaleString()}</span>} />
              )}
              <DetailRow k="ID" v={<span className="mono text-xs">{job.id}</span>} />
              {job.maxRetries > 0 && (
                <DetailRow k="Attempts" v={<span className="mono text-xs">{job.retriedTimes + 1} / {job.maxRetries + 1}</span>} />
              )}
            </div>
          </Panel>
        </div>

        <div className="flex flex-col gap-3.5">
          <JobProgress jobId={job.id} batch={{ totalJobs, completedJobs, failedJobs }} reportedBars={reportedBars} />
          {systemEvents.length > 0 && (
            <Panel>
              <PanelHeader eyebrow="Lifecycle" />
              <div className="px-4 py-3">
                <JobTimeline jobId={job.id} events={systemEvents} />
              </div>
            </Panel>
          )}
          {handlerLogs.length > 0 && <JobLogs jobId={job.id} logs={handlerLogs} />}
        </div>
      </div>

      {hasChildJobs && <RelatedJobsSection job={job} onCountsUpdate={onCountsUpdate} />}
    </div>
  );
}

function DetailRow({ k, v }: { k: string; v: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 border-b border-dashed border-border pb-2 last:border-b-0 last:pb-0">
      <span className="mono text-[10.5px] uppercase tracking-[0.06em] text-text-mute">{k}</span>
      <span className="text-right text-foreground">{v}</span>
    </div>
  );
}
