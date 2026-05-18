import { RelativeTime } from '@/components/RelativeTime';
import type { JobLogModel } from '@/types';

const eventTone: Record<string, { stripe: string; bg: string; text: string }> = {
  Created:    { stripe: 'var(--warp-blue)',   bg: 'rgba(37,99,235,0.06)',   text: 'var(--warp-blue)' },
  Processing: { stripe: 'var(--warp-purple)', bg: 'rgba(124,58,237,0.06)',  text: 'var(--warp-purple)' },
  Completed:  { stripe: 'var(--warp-green)',  bg: 'rgba(22,163,74,0.06)',   text: 'var(--warp-green)' },
  Failed:     { stripe: 'var(--warp-red)',    bg: 'rgba(220,38,38,0.06)',   text: 'var(--warp-red)' },
  Retried:    { stripe: 'var(--warp-amber)',  bg: 'rgba(180,83,9,0.06)',    text: 'var(--warp-amber)' },
  Requeued:   { stripe: 'var(--warp-amber)',  bg: 'rgba(180,83,9,0.06)',    text: 'var(--warp-amber)' },
  Deleted:    { stripe: 'var(--text-mute)',   bg: 'rgba(113,113,122,0.06)', text: 'var(--text-mute)' },
};

function formatDuration(ms: number): string {
  if (ms < 1) {
    return '<1ms';
  }
  if (ms < 1000) {
    return `${Math.round(ms)}ms`;
  }
  if (ms < 60000) {
    return `${(ms / 1000).toFixed(1)}s`;
  }
  const mins = Math.floor(ms / 60000);
  const secs = ((ms % 60000) / 1000).toFixed(0);

  return `${mins}m ${secs}s`;
}

function getDuration(logs: JobLogModel[], currentIndex: number): string | null {
  const log = logs[currentIndex];
  if (log.durationMs != null) {
    return formatDuration(log.durationMs);
  }
  if (currentIndex >= logs.length - 1) {
    return null;
  }
  const current = new Date(logs[currentIndex].timestamp).getTime();
  const previous = new Date(logs[currentIndex + 1].timestamp).getTime();

  return formatDuration(current - previous);
}

interface JobTimelineProps {
  jobId: string;
  events: JobLogModel[];
}

export function JobTimeline({ jobId, events }: JobTimelineProps) {
  if (events.length === 0) {
    return null;
  }

  const jobContext = JSON.stringify({ jobId });

  return (
    <div data-warp-slot="detail.history" data-warp-context={jobContext} key={`history-${jobId}`}>
      <div className="space-y-2">
        {events.map((event, index) => {
          const tone = eventTone[event.eventType] ?? eventTone.Created;
          const duration = getDuration(events, index);

          return (
            <div
              key={event.id}
              className="rounded-md border border-border bg-panel-2 p-3"
              style={{ borderLeft: `3px solid ${tone.stripe}`, background: tone.bg }}
            >
              <div className="flex items-center justify-between">
                <span className="mono text-[11px] font-semibold uppercase tracking-[0.08em]" style={{ color: tone.text }}>
                  {event.eventType}
                </span>
                <span className="text-[11px] text-text-mute">
                  <RelativeTime date={event.timestamp} />
                  {duration && <span className="ml-2 opacity-70">({duration})</span>}
                </span>
              </div>
              {event.message && <p className="mt-1 text-sm text-text-dim">{event.message}</p>}
              {event.exception && (
                <pre className="mono mt-2 max-h-60 overflow-auto rounded-md bg-warp-red-soft p-3 text-[11px] text-warp-red">
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
