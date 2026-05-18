import { Panel, PanelHeader } from '@/components/v2/Panel';

interface BatchProgress {
  totalJobs: number;
  completedJobs: number;
  failedJobs: number;
}

interface JobProgressProps {
  jobId: string;
  batch?: BatchProgress;
  reportedBars?: Array<[string, number]>;
}

export function JobProgress({ jobId, batch, reportedBars }: JobProgressProps) {
  const jobContext = JSON.stringify({ jobId });
  const hasBatch = !!batch && batch.totalJobs > 0;
  const hasBars = !!reportedBars && reportedBars.length > 0;

  if (!hasBatch && !hasBars) {
    return null;
  }

  const done = batch ? batch.completedJobs + batch.failedJobs : 0;
  const pct = hasBatch && batch ? Math.round((done / batch.totalJobs) * 100) : 0;
  const greenPct = hasBatch && batch ? (batch.completedJobs / batch.totalJobs) * 100 : 0;
  const redPct = hasBatch && batch ? (batch.failedJobs / batch.totalJobs) * 100 : 0;

  return (
    <>
      {hasBatch && batch && (
        <div data-warp-slot="detail.progress" data-warp-context={jobContext} key={`progress-${jobId}`}>
          <Panel>
            <PanelHeader
              eyebrow="Progress"
              action={
                <span className="mono text-[11px] text-text-mute">
                  {done}/{batch.totalJobs} · {pct}%
                </span>
              }
            />
            <div className="px-4 py-3">
              <div className="flex h-3 overflow-hidden rounded-full bg-panel-2">
                {greenPct > 0 && (
                  <div className="h-full transition-all" style={{ width: `${greenPct}%`, background: 'var(--warp-green)' }} />
                )}
                {redPct > 0 && (
                  <div className="h-full transition-all" style={{ width: `${redPct}%`, background: 'var(--warp-red)' }} />
                )}
              </div>
            </div>
          </Panel>
        </div>
      )}
      {hasBars && reportedBars && (
        <div data-warp-slot="detail.reportedProgress" data-warp-context={jobContext} key={`reported-progress-${jobId}`}>
          <Panel>
            <PanelHeader eyebrow="Reported progress" />
            <div className="space-y-3 px-4 py-3">
              {reportedBars.map(([name, value]) => (
                <div key={name}>
                  <div className="mb-1 flex items-center justify-between text-xs">
                    <span className="text-text-dim">{name === '' ? 'Progress' : name}</span>
                    <span className="mono font-medium text-foreground">{value}%</span>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-panel-2">
                    <div
                      className="h-full transition-all"
                      style={{ width: `${value}%`, background: 'var(--warp-blue)' }}
                    />
                  </div>
                </div>
              ))}
            </div>
          </Panel>
        </div>
      )}
    </>
  );
}
