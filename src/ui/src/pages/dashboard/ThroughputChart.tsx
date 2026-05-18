import { useEffect, useMemo, useState } from 'react';
import { Panel } from '@/components/v2/Panel';
import { PulseDot } from '@/components/v2/PulseDot';
import { useDashboardStore } from '@/stores/dashboard';
import { areaPath, linePath } from '@/lib/svgPath';

type Range = '1m' | '5m' | '15m' | '1h';

const RANGE_SECONDS: Record<Range, number> = {
  '1m': 60,
  '5m': 300,
  '15m': 900,
  '1h': 3600,
};

const W = 700;
const H = 220;
const PAD_LEFT = 28;
const PAD_BOTTOM = 18;
const PAD_TOP = 8;

export function ThroughputChart() {
  const [range, setRange] = useState<Range>('1m');
  const realtimeData = useDashboardStore((s) => s.realtimeData);

  // RealtimeChart used to own the rate-sampling tick. Keep that contract here
  // so the chart works whether or not the legacy component is mounted.
  useEffect(() => {
    const id = window.setInterval(() => {
      useDashboardStore.getState().sampleRate();
    }, 1000);

    return () => window.clearInterval(id);
  }, []);

  const windowSec = RANGE_SECONDS[range];
  const visible = useMemo(() => realtimeData.slice(-windowSec), [realtimeData, windowSec]);

  const succSeries = useMemo(() => visible.map((p) => p.succeeded), [visible]);
  const failSeries = useMemo(() => visible.map((p) => p.failed), [visible]);

  const now = succSeries.length ? succSeries[succSeries.length - 1] : 0;
  const peak = succSeries.length ? Math.max(...succSeries) : 0;
  const avg = succSeries.length >= 5
    ? Math.round(succSeries.reduce((a, b) => a + b, 0) / succSeries.length)
    : null;

  // Defensive max — at least 10 so an empty/quiet chart still has gridlines.
  const dataMax = Math.max(peak, 10);
  const ticks = useMemo(() => makeTicks(dataMax), [dataMax]);
  const yMax = ticks[ticks.length - 1] ?? dataMax;

  const innerW = W - PAD_LEFT;
  const innerH = H - PAD_BOTTOM - PAD_TOP;

  // Render: if zero points, draw nothing inside the area but keep gridlines.
  const succArea = succSeries.length >= 2 ? areaPath(succSeries, innerW, H - PAD_BOTTOM, PAD_TOP) : '';
  const succLine = succSeries.length >= 2 ? linePath(succSeries, innerW, H - PAD_BOTTOM, PAD_TOP) : '';
  // Failures are typically a fraction of successes — scale up so a small failure
  // count still produces a visible red line. Cap so we don't overflow the box.
  const failScale = succSeries.length && peak > 0 ? Math.max(1, Math.floor(peak / Math.max(1, Math.max(...failSeries.length ? failSeries : [1])))) : 6;
  const failScaled = failSeries.map((v) => Math.min(v * failScale, yMax));
  const failLine = failScaled.length >= 2 ? linePath(failScaled, innerW, H - PAD_BOTTOM, PAD_TOP) : '';

  return (
    <Panel className="flex h-full min-h-[260px] flex-col gap-2 px-4 py-3.5">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <PulseDot />
          <span className="text-[13.5px] font-semibold">Throughput</span>
          <span className="text-[11.5px] text-text-mute">
            jobs / second · {range} window
          </span>
        </div>
        <div className="flex items-center gap-3">
          <div className="mono flex gap-3 text-[11.5px]">
            <span>
              <span className="text-text-mute">now </span>
              <span className="font-semibold text-warp-green">{now}</span>
            </span>
            {avg != null && (
              <span>
                <span className="text-text-mute">avg </span>
                <span className="text-foreground">{avg}</span>
              </span>
            )}
            <span>
              <span className="text-text-mute">peak </span>
              <span className="text-foreground">{peak}</span>
            </span>
          </div>
          <div className="flex gap-0.5 rounded-md bg-panel-2 p-0.5">
            {(Object.keys(RANGE_SECONDS) as Range[]).map((r) => (
              <button
                key={r}
                onClick={() => setRange(r)}
                className={
                  'mono rounded px-2 py-0.5 text-[10.5px] font-semibold transition-colors ' +
                  (range === r
                    ? 'bg-warp-green-soft text-warp-green'
                    : 'text-text-dim hover:text-foreground')
                }
              >
                {r}
              </button>
            ))}
          </div>
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
            <linearGradient id="throughput-grad-g" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--warp-green)" stopOpacity="0.42" />
              <stop offset="100%" stopColor="var(--warp-green)" stopOpacity="0" />
            </linearGradient>
          </defs>
          {ticks.map((tk, i) => {
            const y = PAD_TOP + innerH - (tk / yMax) * innerH;

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
                  fontSize="9.5"
                  textAnchor="end"
                  fontFamily="var(--font-mono)"
                >
                  {tk}
                </text>
              </g>
            );
          })}
          <g transform={`translate(${PAD_LEFT},0)`}>
            {succArea && <path d={succArea} fill="url(#throughput-grad-g)" />}
            {succLine && (
              <path
                d={succLine}
                fill="none"
                stroke="var(--warp-green)"
                strokeWidth={1.7}
                strokeLinecap="round"
              />
            )}
            {failLine && (
              <path
                d={failLine}
                fill="none"
                stroke="var(--warp-red)"
                strokeWidth={1.4}
                opacity={0.9}
                strokeLinecap="round"
              />
            )}
          </g>
        </svg>
        {succSeries.length < 2 && (
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
            <span className="mono text-[11px] text-text-mute">collecting samples…</span>
          </div>
        )}
      </div>
    </Panel>
  );
}

function makeTicks(max: number): number[] {
  // Choose a "nice" upper bound and 4 evenly-spaced ticks.
  const niceMax = niceCeil(max);
  const step = niceMax / 3;

  return [0, step, step * 2, niceMax].map((v) => Math.round(v));
}

function niceCeil(v: number): number {
  if (v <= 10) {
    return 10;
  }
  const mag = Math.pow(10, Math.floor(Math.log10(v)));
  const n = v / mag;
  let nice;
  if (n <= 1.5) {
    nice = 1.5;
  } else if (n <= 2) {
    nice = 2;
  } else if (n <= 3) {
    nice = 3;
  } else if (n <= 5) {
    nice = 5;
  } else {
    nice = 10;
  }

  return nice * mag;
}
