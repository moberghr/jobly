import { PulseDot } from './PulseDot';

type LivePillProps = {
  /** Secondary value (e.g. polling interval, latency). Rendered in `text-mute`. */
  detail?: string;
  /** Visual variant. `live` = green, `idle` = amber, `disconnected` = red. */
  state?: 'live' | 'idle' | 'disconnected';
  className?: string;
};

const variants = {
  live: {
    dot: 'text-warp-green',
    label: 'text-warp-green',
    bg: 'bg-warp-green-soft',
    ring: 'ring-warp-green/30',
    text: 'LIVE',
  },
  idle: {
    dot: 'text-warp-amber',
    label: 'text-warp-amber',
    bg: 'bg-warp-amber-soft',
    ring: 'ring-warp-amber/30',
    text: 'IDLE',
  },
  disconnected: {
    dot: 'text-warp-red',
    label: 'text-warp-red',
    bg: 'bg-warp-red-soft',
    ring: 'ring-warp-red/30',
    text: 'OFFLINE',
  },
} as const;

/**
 * Realtime status pill rendered in the topbar. Pulses while `state === 'live'`.
 */
export function LivePill({ detail, state = 'live', className }: LivePillProps) {
  const v = variants[state];

  return (
    <div
      className={[
        'inline-flex items-center gap-2 rounded-full px-2.5 py-1',
        'ring-1',
        v.bg,
        v.ring,
        className ?? '',
      ].join(' ')}
    >
      <PulseDot colorClass={v.dot} size={5} />
      <span className={`mono text-[10.5px] font-semibold uppercase tracking-wider ${v.label}`}>
        {v.text}
      </span>
      {detail && <span className="mono text-[11px] text-text-mute">{detail}</span>}
    </div>
  );
}
