import type { JobLogModel } from '@/types';

interface AttemptEvent {
  label: string;
  time: string;
  ok: boolean;
  durationMs: number | null;
  kind: 'create' | 'fail' | 'final';
  waitSeconds?: number | null;
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  const hh = String(d.getUTCHours()).padStart(2, '0');
  const mm = String(d.getUTCMinutes()).padStart(2, '0');
  const ss = String(d.getUTCSeconds()).padStart(2, '0');

  return `${hh}:${mm}:${ss}`;
}

function formatDuration(ms: number | null): string | null {
  if (ms == null) {
    return null;
  }
  if (ms < 1000) {
    return `${Math.round(ms)}ms`;
  }
  if (ms < 60000) {
    return `${(ms / 1000).toFixed(1)}s`;
  }
  const mins = Math.floor(ms / 60000);
  const secs = Math.floor((ms % 60000) / 1000);

  return `${mins}m ${secs}s`;
}

function buildEvents(logs: JobLogModel[]): AttemptEvent[] {
  if (logs.length === 0) {
    return [];
  }

  const sorted = [...logs].sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime());
  const events: AttemptEvent[] = [];
  let attempt = 0;

  for (const l of sorted) {
    if (l.eventType === 'Created') {
      events.push({ label: 'Created', time: formatTime(l.timestamp), ok: true, durationMs: null, kind: 'create' });
    } else if (l.eventType === 'Failed' || l.eventType === 'Retried') {
      attempt += 1;
      events.push({
        label: `Attempt ${attempt}`,
        time: formatTime(l.timestamp),
        ok: false,
        durationMs: l.durationMs ?? null,
        kind: 'fail',
      });
    }
  }

  const lastFail = [...sorted].reverse().find(x => x.eventType === 'Failed');
  if (lastFail) {
    events.push({
      label: 'Stopped',
      time: formatTime(lastFail.timestamp),
      ok: false,
      durationMs: null,
      kind: 'final',
    });
  }

  if (events.length === 0) {
    const first = sorted[0];
    events.push({ label: 'Created', time: formatTime(first.timestamp), ok: true, durationMs: null, kind: 'create' });
    events.push({ label: 'Stopped', time: formatTime(first.timestamp), ok: false, durationMs: null, kind: 'final' });
  }

  for (let j = 1; j < events.length; j++) {
    const a = j - 1 < sorted.length ? sorted[j - 1]?.timestamp : null;
    const b = j < sorted.length ? sorted[j]?.timestamp : null;
    if (a && b) {
      const diffMs = new Date(b).getTime() - new Date(a).getTime();
      events[j].waitSeconds = Math.max(0, Math.round(diffMs / 1000));
    }
  }

  return events;
}

interface AttemptTimelineRibbonProps {
  logs: JobLogModel[];
  totalDurationMs: number | null;
}

export function AttemptTimelineRibbon({ logs, totalDurationMs }: AttemptTimelineRibbonProps) {
  const events = buildEvents(logs);
  const W = 1170;
  const H = 168;
  const padX = 60;
  const yMid = 92;
  const xAt = (i: number) => padX + (i / Math.max(1, events.length - 1)) * (W - padX * 2);

  return (
    <div className="warp-panel relative mb-3.5 overflow-hidden px-4 py-3.5">
      <div className="mb-1.5 flex items-center justify-between">
        <span className="warp-eyebrow">
          Attempt timeline{totalDurationMs ? ` · ${formatDuration(totalDurationMs)} lifecycle` : ''}
        </span>
        <span className="mono text-[11px] text-text-mute">
          {events.length - 1} {events.length - 1 === 1 ? 'attempt' : 'attempts'}
        </span>
      </div>
      <div className="overflow-x-auto">
        <svg
          width={W}
          height={H}
          viewBox={`0 0 ${W} ${H}`}
          style={{ minWidth: 720, width: '100%', display: 'block' }}
        >
          <defs>
            <linearGradient id="atr-trk" x1="0" y1="0" x2="1" y2="0">
              <stop offset="0%" stopColor="var(--warp-blue)" stopOpacity="0.7" />
              <stop offset="20%" stopColor="var(--warp-red)" stopOpacity="0.8" />
              <stop offset="100%" stopColor="var(--warp-red)" stopOpacity="0.95" />
            </linearGradient>
          </defs>
          <line x1={padX} y1={yMid} x2={W - padX} y2={yMid} stroke="var(--border)" strokeWidth={2} />
          <line x1={padX} y1={yMid} x2={W - padX} y2={yMid} stroke="url(#atr-trk)" strokeWidth={2} strokeOpacity={0.7} />
          {events.map((e, i) => {
            if (i === 0 || e.ok) {
              return null;
            }
            const x0 = xAt(i - 1);
            const x1 = xAt(i);
            const mid = (x0 + x1) / 2;

            return (
              <g key={`ekg-${i}`}>
                <path
                  d={`M ${x0 + 8} ${yMid} L ${mid - 18} ${yMid} L ${mid - 8} ${yMid - 28} L ${mid + 4} ${yMid + 32} L ${mid + 14} ${yMid} L ${x1 - 8} ${yMid}`}
                  fill="none"
                  stroke="var(--warp-red)"
                  strokeWidth={1.6}
                  opacity={0.55}
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
                {e.waitSeconds != null && e.waitSeconds > 0 && (
                  <text x={mid} y={yMid - 36} fill="var(--text-mute)" fontSize="10" textAnchor="middle" fontFamily="Geist Mono">
                    wait {e.waitSeconds}s
                  </text>
                )}
              </g>
            );
          })}
          {events.map((e, i) => {
            const x = xAt(i);
            const color = e.kind === 'create' ? 'var(--warp-blue)' : 'var(--warp-red)';
            const isFinal = e.kind === 'final';
            const dur = formatDuration(e.durationMs);

            return (
              <g key={i}>
                <circle cx={x} cy={yMid} r={isFinal ? 18 : 12} fill={color} opacity={0.18} />
                <circle cx={x} cy={yMid} r={isFinal ? 11 : 8} fill="var(--panel)" stroke={color} strokeWidth={2.4} />
                {!e.ok && !isFinal && (
                  <g transform={`translate(${x},${yMid})`}>
                    <path d="M -3 -3 L 3 3 M 3 -3 L -3 3" stroke={color} strokeWidth={1.8} strokeLinecap="round" />
                  </g>
                )}
                {isFinal && (
                  <g transform={`translate(${x},${yMid})`}>
                    <circle cx="0" cy="0" r="11" fill="var(--warp-red)" />
                    <path d="M -3 -3 L 3 3 M 3 -3 L -3 3" stroke="#fff" strokeWidth={2} strokeLinecap="round" />
                  </g>
                )}
                <text x={x} y={yMid - 26} fill="var(--foreground)" fontSize="12" textAnchor="middle" fontWeight="600" fontFamily="Geist">{e.label}</text>
                <text x={x} y={yMid + 30} fill="var(--text-dim)" fontSize="10.5" textAnchor="middle" fontFamily="Geist Mono">{e.time}</text>
                {dur && <text x={x} y={yMid + 44} fill="var(--text-mute)" fontSize="10" textAnchor="middle" fontFamily="Geist Mono">{dur}</text>}
                {!e.ok && !isFinal && (
                  <text x={x} y={yMid + 58} fill="var(--warp-red)" fontSize="10" textAnchor="middle" fontFamily="Geist Mono" fontWeight="600" letterSpacing="0.8">FAILED</text>
                )}
                {isFinal && (
                  <text x={x} y={yMid + 44} fill="var(--warp-red)" fontSize="10" textAnchor="middle" fontFamily="Geist Mono" fontWeight="700" letterSpacing="0.8">STOPPED</text>
                )}
              </g>
            );
          })}
        </svg>
      </div>
    </div>
  );
}
