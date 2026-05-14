import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';

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
  const hasBatch = batch && batch.totalJobs > 0;
  const hasBars = reportedBars && reportedBars.length > 0;

  if (!hasBatch && !hasBars) return null;

  const done = batch ? batch.completedJobs + batch.failedJobs : 0;
  const pct = hasBatch ? Math.round((done / batch.totalJobs) * 100) : 0;
  const greenPct = hasBatch ? (batch.completedJobs / batch.totalJobs) * 100 : 0;
  const redPct = hasBatch ? (batch.failedJobs / batch.totalJobs) * 100 : 0;

  return (
    <>
      {hasBatch && (
        <div data-warp-slot="detail.progress" data-warp-context={jobContext} key={`progress-${jobId}`}>
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Progress</CardTitle></CardHeader>
            <CardContent>
              <div className="flex items-center gap-4">
                <div className="flex-1 h-4 bg-muted rounded-full overflow-hidden flex">
                  {greenPct > 0 && <div className="h-full bg-green-500 transition-all" style={{ width: `${greenPct}%` }} />}
                  {redPct > 0 && <div className="h-full bg-red-500 transition-all" style={{ width: `${redPct}%` }} />}
                </div>
                <span className="text-sm font-medium">{done}/{batch.totalJobs} ({pct}%)</span>
              </div>
            </CardContent>
          </Card>
        </div>
      )}
      {hasBars && (
        <div data-warp-slot="detail.reportedProgress" data-warp-context={jobContext} key={`reported-progress-${jobId}`}>
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Reported Progress</CardTitle></CardHeader>
            <CardContent>
              <div className="space-y-2">
                {reportedBars!.map(([name, value]) => (
                  <div key={name}>
                    <div className="flex items-center justify-between text-xs mb-1">
                      <span className="text-muted-foreground">{name === '' ? 'Progress' : name}</span>
                      <span className="font-medium">{value}%</span>
                    </div>
                    <div className="h-2 bg-muted rounded-full overflow-hidden">
                      <div className="h-full bg-blue-500 transition-all" style={{ width: `${value}%` }} />
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </div>
      )}
    </>
  );
}
