import { RelativeTime } from '@/components/RelativeTime';
import type { JobLogModel } from '@/types';

const eventColors: Record<string, { border: string; bg: string; text: string }> = {
  Created:    { border: 'border-l-blue-500', bg: 'bg-blue-50 dark:bg-blue-950/30', text: 'text-blue-700 dark:text-blue-400' },
  Processing: { border: 'border-l-purple-500', bg: 'bg-purple-50 dark:bg-purple-950/30', text: 'text-purple-700 dark:text-purple-400' },
  Completed:  { border: 'border-l-green-500', bg: 'bg-green-50 dark:bg-green-950/30', text: 'text-green-700 dark:text-green-400' },
  Failed:     { border: 'border-l-red-500', bg: 'bg-red-50 dark:bg-red-950/30', text: 'text-red-700 dark:text-red-400' },
  Requeued:   { border: 'border-l-yellow-500', bg: 'bg-yellow-50 dark:bg-yellow-950/30', text: 'text-yellow-700 dark:text-yellow-400' },
  Deleted:    { border: 'border-l-gray-500', bg: 'bg-gray-50 dark:bg-gray-950/30', text: 'text-gray-700 dark:text-gray-400' },
};

function formatDuration(ms: number): string {
  if (ms < 1) return '<1ms';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  const mins = Math.floor(ms / 60000);
  const secs = ((ms % 60000) / 1000).toFixed(0);
  return `${mins}m ${secs}s`;
}

function getDuration(logs: JobLogModel[], currentIndex: number): string | null {
  const log = logs[currentIndex];
  if (log.durationMs != null) return formatDuration(log.durationMs);
  if (currentIndex >= logs.length - 1) return null;
  const current = new Date(logs[currentIndex].timestamp).getTime();
  const previous = new Date(logs[currentIndex + 1].timestamp).getTime();
  return formatDuration(current - previous);
}

interface JobTimelineProps {
  jobId: string;
  events: JobLogModel[];
}

export function JobTimeline({ jobId, events }: JobTimelineProps) {
  if (events.length === 0) return null;

  const jobContext = JSON.stringify({ jobId });

  return (
    <div data-warp-slot="detail.history" data-warp-context={jobContext} key={`history-${jobId}`}>
      <h2 className="text-sm font-semibold text-muted-foreground uppercase mb-3">History</h2>
      <div className="space-y-3">
        {events.map((event, index) => {
          const colors = eventColors[event.eventType] ?? eventColors.Created;
          const duration = getDuration(events, index);
          return (
            <div key={event.id} className={`border-l-4 ${colors.border} ${colors.bg} rounded-r-md p-4`}>
              <div className="flex items-center justify-between">
                <span className={`font-semibold ${colors.text}`}>{event.eventType}</span>
                <span className="text-xs text-muted-foreground">
                  <RelativeTime date={event.timestamp} />
                  {duration && <span className="ml-2 opacity-60">({duration})</span>}
                </span>
              </div>
              {event.message && <p className="text-sm text-muted-foreground mt-1">{event.message}</p>}
              {event.exception && (
                <pre className="text-xs bg-red-100 dark:bg-red-950/50 text-red-800 dark:text-red-300 p-3 rounded-md overflow-auto mt-2 max-h-60">
                  {event.exception}
                </pre>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
