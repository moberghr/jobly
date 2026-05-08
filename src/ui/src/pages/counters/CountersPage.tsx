import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { Chart, LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip as ChartTooltip, Legend } from 'chart.js';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { LoadingState, ErrorState } from '@/components/PageState';
import { useRefreshKey } from '@/hooks/useRefreshKey';
import type { CounterModel, CounterHistoryPoint } from '@/types';
import * as api from '@/api';

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, ChartTooltip, Legend);

const builtInColors: Record<string, string> = {
  'stats:succeeded': '#22c55e',
  'stats:failed': '#ef4444',
  'stats:deleted': '#9ca3af',
  'stats:requeued': '#f59e0b',
};

// Deterministic color from key for addon-defined metrics. Same key → same color across reloads.
function colorFor(key: string): string {
  if (builtInColors[key]) return builtInColors[key];
  let hash = 0;
  for (let i = 0; i < key.length; i++) {
    hash = (hash * 31 + key.charCodeAt(i)) | 0;
  }
  const hue = Math.abs(hash) % 360;
  return `hsl(${hue}, 65%, 50%)`;
}

export default function CountersPage() {
  const [counters, setCounters] = useState<CounterModel[] | null>(null);
  const [history, setHistory] = useState<CounterHistoryPoint[] | null>(null);
  const [historyHours, setHistoryHours] = useState(24);
  const [error, setError] = useState<string | null>(null);
  const refreshKey = useRefreshKey();

  const fetchAll = useCallback(() => {
    api.getCounters().then(setCounters).catch(() => setError('Unable to load counters'));
    api.getCountersHistory(historyHours).then(setHistory).catch(() => {});
  }, [historyHours]);

  useEffect(() => {
    fetchAll();
    const id = setInterval(fetchAll, 5000);
    return () => clearInterval(id);
  }, [refreshKey, fetchAll]);

  if (error) return <ErrorState message={error} />;
  if (!counters) return <LoadingState />;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-2">Counters</h1>
      <p className="text-sm text-muted-foreground mb-4">
        Raw counter rows from the database. Built-in: <code>stats:succeeded</code>,{' '}
        <code>stats:failed</code>, <code>stats:deleted</code>, <code>stats:requeued</code>.
        Addons can write their own keys here.
      </p>

      <Card className="mb-6">
        <CardHeader className="pb-2 flex-row items-center justify-between space-y-0">
          <CardTitle className="text-base">Hourly history</CardTitle>
          <div className="flex gap-1">
            {[
              { label: '24h', hours: 24 },
              { label: '7d', hours: 168 },
            ].map(({ label, hours }) => (
              <button
                key={label}
                onClick={() => setHistoryHours(hours)}
                className={`px-2 py-0.5 text-xs rounded-md transition-colors ${
                  historyHours === hours
                    ? 'bg-primary text-primary-foreground'
                    : 'text-muted-foreground hover:bg-accent'
                }`}
              >
                {label}
              </button>
            ))}
          </div>
        </CardHeader>
        <CardContent>
          <HistoryChart points={history} hours={historyHours} />
        </CardContent>
      </Card>

      {counters.length === 0 ? (
        <Card>
          <CardContent className="py-8 text-center text-muted-foreground">
            No counters
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="p-0">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50">
                <tr>
                  <th className="text-left font-semibold px-4 py-2">Key</th>
                  <th className="text-right font-semibold px-4 py-2 w-40">Value</th>
                </tr>
              </thead>
              <tbody>
                {counters.map((c) => (
                  <tr key={c.key} className="border-b last:border-b-0 hover:bg-muted/30">
                    <td className="px-4 py-2 font-mono">{c.key}</td>
                    <td className="px-4 py-2 text-right font-mono">{c.value.toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

function HistoryChart({ points, hours }: { points: CounterHistoryPoint[] | null; hours: number }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const chartRef = useRef<Chart | null>(null);

  // Pivot points → labels + per-key series, padding empty hours with 0.
  const data = useMemo(() => {
    if (!points) return null;

    const now = new Date();
    now.setMinutes(0, 0, 0);

    const labels: string[] = [];
    const hourTimes: number[] = [];
    for (let i = hours - 1; i >= 0; i--) {
      const t = now.getTime() - i * 3600000;
      hourTimes.push(t);
      const d = new Date(t);
      labels.push(
        hours <= 24
          ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })
          : `${d.toLocaleDateString([], { weekday: 'short' })} ${String(d.getDate()).padStart(2, '0')}`
      );
    }

    const seriesMap = new Map<string, number[]>();
    for (const p of points) {
      const t = new Date(p.hour).getTime();
      const idx = hourTimes.indexOf(t);
      if (idx < 0) continue;

      let series = seriesMap.get(p.key);
      if (!series) {
        series = new Array(hours).fill(0);
        seriesMap.set(p.key, series);
      }
      series[idx] = p.value;
    }

    const series = [...seriesMap.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([key, values]) => ({ key, values, color: colorFor(key) }));

    return { labels, series };
  }, [points, hours]);

  useEffect(() => {
    if (!canvasRef.current) return;

    const isDark = document.documentElement.classList.contains('dark');
    const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.08)';
    const textColor = isDark ? '#888' : '#666';

    chartRef.current = new Chart(canvasRef.current, {
      type: 'line',
      data: { labels: [], datasets: [] },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: { ticks: { color: textColor, font: { size: 10 }, maxRotation: 0, autoSkip: true, maxTicksLimit: 24 }, grid: { color: gridColor } },
          y: { beginAtZero: true, ticks: { color: textColor, font: { size: 10 }, precision: 0 }, grid: { color: gridColor } },
        },
        plugins: {
          legend: {
            display: true,
            position: 'top' as const,
            labels: { color: textColor, font: { size: 11 }, boxWidth: 12, boxHeight: 12 },
          },
          tooltip: {
            backgroundColor: isDark ? '#1f1f23' : '#fff',
            titleColor: isDark ? '#e4e4e7' : '#18181b',
            bodyColor: isDark ? '#a1a1aa' : '#52525b',
            borderColor: isDark ? '#27272a' : '#e4e4e7',
            borderWidth: 1,
          },
        },
      },
    });

    return () => { chartRef.current?.destroy(); chartRef.current = null; };
  }, []);

  useEffect(() => {
    if (!chartRef.current || !data) return;
    chartRef.current.data.labels = data.labels;
    chartRef.current.data.datasets = data.series.map(s => ({
      label: s.key,
      data: s.values,
      borderColor: s.color,
      backgroundColor: s.color + '22',
      borderWidth: 2,
      fill: false,
      pointRadius: 0,
      pointHitRadius: 10,
      tension: 0.3,
    }));
    chartRef.current.update();
  }, [data]);

  if (!points) {
    return <div style={{ height: 240 }} className="flex items-center justify-center text-sm text-muted-foreground">Loading...</div>;
  }

  if (data && data.series.length === 0) {
    return <div style={{ height: 240 }} className="flex items-center justify-center text-sm text-muted-foreground">No hourly data yet</div>;
  }

  return (
    <div style={{ height: 240 }}>
      <canvas ref={canvasRef} />
    </div>
  );
}
