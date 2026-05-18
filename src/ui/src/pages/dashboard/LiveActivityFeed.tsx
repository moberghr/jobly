import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Panel } from '@/components/v2/Panel';
import { subscribeRealtime } from '@/lib/realtimeBus';
import type { RealtimeEvent } from '@/api/realtime';

type FeedRow = {
  id: number;
  time: string;
  event: RealtimeEvent;
};

const KIND: Record<RealtimeEvent, { dot: string; label: string }> = {
  JobFinalized: { dot: 'bg-warp-green', label: 'Job finalized' },
  MessageEnqueued: { dot: 'bg-warp-purple', label: 'Message enqueued' },
};

/**
 * Live activity feed. The realtime bus emits payload-free events (jobs
 * finalized, messages enqueued), so we render a generic "event happened" row
 * rather than fabricating per-job details. When the bus is extended to carry
 * job/queue info, this is the place to enrich the row.
 */
export function LiveActivityFeed() {
  const [rows, setRows] = useState<FeedRow[]>([]);

  useEffect(() => {
    let counter = 0;

    const push = (event: RealtimeEvent) => {
      setRows((prev) => {
        counter += 1;
        const next: FeedRow = {
          id: counter,
          time: new Date().toLocaleTimeString([], {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false,
          }),
          event,
        };

        return [next, ...prev].slice(0, 10);
      });
    };

    const offJob = subscribeRealtime('JobFinalized', () => push('JobFinalized'));
    const offMsg = subscribeRealtime('MessageEnqueued', () => push('MessageEnqueued'));

    return () => {
      offJob();
      offMsg();
    };
  }, []);

  return (
    <Panel className="flex h-full flex-col overflow-hidden pt-3.5 pb-1.5">
      <div className="flex items-center justify-between px-4 pb-2.5">
        <div className="flex items-center gap-2">
          <span className="text-[13.5px] font-semibold">Live activity</span>
          <span className="text-[11px] text-text-mute">last 10 events</span>
        </div>
        <Link
          to="/jobs/enqueued"
          className="mono text-[11px] text-text-dim hover:text-foreground"
        >
          view all →
        </Link>
      </div>
      <div className="flex-1 overflow-hidden">
        {rows.length === 0 && (
          <div className="px-4 py-6 text-[11.5px] text-text-mute">
            Waiting for realtime events…
          </div>
        )}
        {rows.map((r, i) => {
          const k = KIND[r.event];

          return (
            <div
              key={r.id}
              className={
                'flex items-center gap-2.5 border-b border-border px-4 py-1.5 ' +
                (i === 0 ? 'border-t' : '')
              }
            >
              <span
                aria-hidden="true"
                className={`h-1.5 w-1.5 flex-shrink-0 rounded-full ${k.dot}`}
              />
              <span className="mono w-[60px] flex-shrink-0 text-[10.5px] text-text-mute">
                {r.time}
              </span>
              <span className="mono rounded border border-border bg-panel-2 px-1.5 py-px text-[10.5px] text-text-dim">
                {r.event === 'JobFinalized' ? 'job' : 'msg'}
              </span>
              <span className="truncate text-[11.5px] text-foreground">{k.label}</span>
            </div>
          );
        })}
      </div>
    </Panel>
  );
}
