import { useRef, useEffect, useState } from 'react';
import {
  Chart,
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  TimeScale,
  Filler,
} from 'chart.js';
import 'chartjs-adapter-luxon';
import { useDashboardStore } from '@/stores/dashboard';

Chart.register(LineController, LineElement, PointElement, LinearScale, TimeScale, Filler);

type Range = '1m' | '5m' | '15m' | '1h';

const RANGE_SECONDS: Record<Range, number> = {
  '1m': 60,
  '5m': 300,
  '15m': 900,
  '1h': 3600,
};

export function RealtimeChart({ height = 200 }: { height?: number }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const chartRef = useRef<Chart | null>(null);
  const lastRenderedTs = useRef(0);
  const rafId = useRef(0);
  const [range, setRange] = useState<Range>('1m');
  const rangeRef = useRef<Range>(range);
  rangeRef.current = range;
  const realtimeData = useDashboardStore((s) => s.realtimeData);

  const windowSec = RANGE_SECONDS[range];
  const visible = realtimeData.slice(-windowSec);
  const vals = visible.map((p) => p.succeeded + p.failed);
  const current = vals.length > 0 ? vals[vals.length - 1] : 0;
  const max = vals.length > 0 ? Math.max(...vals) : 0;
  const avg = vals.length >= 5 ? Math.round(vals.reduce((a, b) => a + b, 0) / vals.length) : null;

  useEffect(() => {
    if (!canvasRef.current) return;

    const isDark = document.documentElement.classList.contains('dark');
    const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.08)';
    const textColor = isDark ? '#888' : '#666';
    const now = Date.now();

    const storeData = useDashboardStore.getState().realtimeData;
    if (storeData.length > 0) {
      lastRenderedTs.current = storeData[storeData.length - 1].ts;
    }

    chartRef.current = new Chart(canvasRef.current, {
      type: 'line',
      data: {
        datasets: [
          {
            label: 'Succeeded/s',
            borderColor: '#22c55e',
            backgroundColor: 'rgba(34, 197, 94, 0.15)',
            borderWidth: 2,
            fill: true,
            pointRadius: 0,
            tension: 0.3,
            data: storeData.map((p) => ({ x: p.ts * 1000, y: p.succeeded })),
          },
          {
            label: 'Failed/s',
            borderColor: '#ef4444',
            backgroundColor: 'rgba(239, 68, 68, 0.15)',
            borderWidth: 2,
            fill: true,
            pointRadius: 0,
            tension: 0.3,
            data: storeData.map((p) => ({ x: p.ts * 1000, y: p.failed })),
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        events: [],
        scales: {
          x: {
            type: 'time',
            time: { unit: 'second', displayFormats: { second: 'HH:mm:ss' } },
            min: now - (windowSec + 1) * 1000,
            max: now - 1000,
            ticks: { display: false },
            grid: { color: gridColor },
          },
          y: {
            beginAtZero: true,
            ticks: { color: textColor, font: { size: 10 }, precision: 0 },
            grid: { color: gridColor },
          },
        },
        plugins: { legend: { display: false }, tooltip: { enabled: false } },
      },
    });

    // Scroll the visible window — width is driven by rangeRef so range changes
    // take effect on the next frame without rebuilding the chart.
    const scroll = () => {
      if (!chartRef.current) return;
      const t = Date.now();
      const w = RANGE_SECONDS[rangeRef.current];
      const xScale = chartRef.current.options.scales!.x!;
      xScale.min = t - (w + 2) * 1000;
      xScale.max = t - 2000;
      chartRef.current.update('none');
      rafId.current = requestAnimationFrame(scroll);
    };
    rafId.current = requestAnimationFrame(scroll);

    return () => {
      cancelAnimationFrame(rafId.current);
      chartRef.current?.destroy();
      chartRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Push new data points from store
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) return;

    const data = useDashboardStore.getState().realtimeData;
    const now = Date.now();

    for (const p of data) {
      if (p.ts <= lastRenderedTs.current) continue;
      (chart.data.datasets[0].data as { x: number; y: number }[]).push({ x: p.ts * 1000, y: p.succeeded });
      (chart.data.datasets[1].data as { x: number; y: number }[]).push({ x: p.ts * 1000, y: p.failed });
      lastRenderedTs.current = p.ts;
    }

    // Trim points older than the maximum supported window (1h) with a small buffer.
    const cutoff = now - (RANGE_SECONDS['1h'] + 60) * 1000;
    for (const ds of chart.data.datasets) {
      const arr = ds.data as { x: number; y: number }[];
      while (arr.length > 0 && arr[0].x < cutoff) arr.shift();
    }
  }, [realtimeData]);

  // 1Hz sampling tick — independent of event-driven stats refresh.
  useEffect(() => {
    const id = setInterval(() => {
      useDashboardStore.getState().sampleRate();
    }, 1000);
    return () => clearInterval(id);
  }, []);

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <div className="flex gap-6 text-sm">
          <span className="text-muted-foreground">Current: <span className="font-medium text-foreground">{current}/s</span></span>
          <span className="text-muted-foreground">Avg: <span className="font-medium text-foreground">{avg != null ? `${avg}/s` : '-'}</span></span>
          <span className="text-muted-foreground">Peak: <span className="font-medium text-foreground">{max}/s</span></span>
        </div>
        <div className="flex gap-1" role="group" aria-label="Time range">
          {(Object.keys(RANGE_SECONDS) as Range[]).map((r) => (
            <button
              key={r}
              onClick={() => setRange(r)}
              aria-pressed={range === r}
              className={`px-2 py-0.5 text-xs rounded-md transition-colors ${
                range === r
                  ? 'bg-primary text-primary-foreground'
                  : 'text-muted-foreground hover:bg-accent'
              }`}
            >
              {r}
            </button>
          ))}
        </div>
      </div>
      <div style={{ height }}>
        <canvas ref={canvasRef} />
      </div>
    </div>
  );
}
