import { useMemo } from 'react';
import { Panel } from '@/components/v2/Panel';
import { useStatsHistory } from '@/api/hooks/useDashboard';

const W = 700;
const H = 168;
const PAD_LEFT = 36;
const PAD_BOTTOM = 20;
const PAD_TOP = 10;

/**
 * 24h bar chart of completed jobs per hour. Bars where any failure was recorded
 * get a thin red top-band so they're visually distinct from a clean hour.
 */
export function HistoryChart() {
  const { data, isLoading } = useStatsHistory(24);

  const series = useMemo(() => padHistory(data ?? []), [data]);
  const totals = useMemo(() => {
    let s = 0;
    let f = 0;
    for (const p of series) {
      s += p.succeeded;
      f += p.failed;
    }

    return { succeeded: s, failed: f };
  }, [series]);

  const max = Math.max(...series.map((p) => p.succeeded), 1) * 1.15;
  const innerH = H - PAD_BOTTOM - PAD_TOP;
  const innerW = W - PAD_LEFT - 4;
  const barW = innerW / series.length;
  const ticks = [0, Math.round(max / 2), Math.round(max)];

  return (
    <Panel className="flex h-full flex-col px-3.5 py-3">
      <div className="mb-1 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="text-[13.5px] font-semibold">History</span>
          <span className="text-[11.5px] text-text-mute">completed · 24h</span>
        </div>
        <div className="mono text-[11.5px] text-text-dim">
          <span className="text-warp-green">{totals.succeeded.toLocaleString()}</span> succeeded
          {' · '}
          <span className="text-warp-red">{totals.failed.toLocaleString()}</span> failed
        </div>
      </div>
      <div className="relative flex-1">
        <svg
          width="100%"
          height={H}
          viewBox={`0 0 ${W} ${H}`}
          preserveAspectRatio="none"
          className="block"
          aria-hidden="true"
        >
          <defs>
            <linearGradient id="history-grad-g" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--warp-green)" stopOpacity="0.95" />
              <stop offset="100%" stopColor="var(--warp-green)" stopOpacity="0.3" />
            </linearGradient>
          </defs>
          {ticks.map((tk, i) => {
            const y = PAD_TOP + innerH - (tk / max) * innerH;

            return (
              <g key={i}>
                <line
                  x1={PAD_LEFT}
                  y1={y}
                  x2={W}
                  y2={y}
                  stroke="var(--border)"
                  strokeDasharray="2 4"
                />
                <text
                  x={PAD_LEFT - 6}
                  y={y + 3.5}
                  fill="var(--text-mute)"
                  fontSize="9"
                  textAnchor="end"
                  fontFamily="var(--font-mono)"
                >
                  {tk}
                </text>
              </g>
            );
          })}
          {series.map((d, i) => {
            const x = PAD_LEFT + i * barW;
            const bh = (d.succeeded / max) * innerH;
            const y = PAD_TOP + innerH - bh;
            const hour = new Date(d.hour).getHours();

            return (
              <g key={i}>
                <rect
                  x={x + 1.5}
                  y={y}
                  width={Math.max(0, barW - 5)}
                  height={bh}
                  rx={2}
                  fill="url(#history-grad-g)"
                />
                {d.failed > 0 && (
                  <rect
                    x={x + 1.5}
                    y={PAD_TOP + innerH - 2.5}
                    width={Math.max(0, barW - 5)}
                    height={2.5}
                    fill="var(--warp-red)"
                  />
                )}
                {i % 4 === 0 && (
                  <text
                    x={x + barW / 2}
                    y={H - 5}
                    fill="var(--text-mute)"
                    fontSize="9"
                    textAnchor="middle"
                    fontFamily="var(--font-mono)"
                  >
                    {String(hour).padStart(2, '0')}
                  </text>
                )}
              </g>
            );
          })}
        </svg>
        {isLoading && !data && (
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
            <span className="mono text-[11px] text-text-mute">loading…</span>
          </div>
        )}
      </div>
    </Panel>
  );
}

function padHistory(data: { hour: string; succeeded: number; failed: number }[]) {
  const now = new Date();
  now.setMinutes(0, 0, 0);
  const map = new Map(data.map((d) => [new Date(d.hour).getTime(), d]));
  const out: { hour: string; succeeded: number; failed: number }[] = [];
  for (let i = 23; i >= 0; i--) {
    const h = new Date(now.getTime() - i * 3600000);
    const p = map.get(h.getTime());
    out.push({
      hour: h.toISOString(),
      succeeded: p?.succeeded ?? 0,
      failed: p?.failed ?? 0,
    });
  }

  return out;
}
