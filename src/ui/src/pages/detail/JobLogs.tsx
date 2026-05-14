import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { formatDateTime } from '@/utils/format';
import type { JobLogModel } from '@/types';

interface JobLogsProps {
  jobId: string;
  logs: JobLogModel[];
}

export function JobLogs({ jobId, logs }: JobLogsProps) {
  if (logs.length === 0) return null;

  const jobContext = JSON.stringify({ jobId });

  return (
    <div data-warp-slot="detail.logs" data-warp-context={jobContext} key={`logs-${jobId}`}>
      <Card>
        <CardHeader className="pb-2"><CardTitle className="text-sm">Handler Output ({logs.length})</CardTitle></CardHeader>
        <CardContent>
          <div className="space-y-1 font-mono text-xs max-h-[80vh] overflow-auto">
            {logs.map((log) => (
              <div key={log.id} className={`flex gap-2 ${
                log.level === 'Error' ? 'text-red-600' :
                log.level === 'Warning' ? 'text-yellow-600' :
                'text-muted-foreground'
              }`}>
                <span className="text-muted-foreground shrink-0">{formatDateTime(log.timestamp)}</span>
                <span className="shrink-0 w-20">[{log.level}]</span>
                <span className="break-all">{log.message}</span>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
