import { useState, useEffect, useRef, useMemo } from 'react';
import { Chart, LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip as ChartTooltip, Legend } from 'chart.js';
import { useDashboardStore } from '@/stores/dashboard';
import { MetricCard } from '@/components/MetricCard';
import { RealtimeChart } from '@/components/RealtimeChart';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { DashboardSkeleton } from '@/components/skeletons/DashboardSkeleton';
import { useStatsHistory } from '@/api/hooks/useDashboard';
import type { StatsHistoryPoint } from '@/types';
import {
  Briefcase,
  XCircle,
  Clock,
  Mail,
  Loader,
  Layers,
} from 'lucide-react';

function padHistory(data: StatsHistoryPoint[], hours: number) {
  const now = new Date();
  now.setMinutes(0, 0, 0);
  const dataMap = new Map(data.map(d => [new Date(d.hour).getTime(), d]));

  if (hours <= 24) {
    const result = [];
    for (let i = hours - 1; i >= 0; i--) {
      const hourDate = new Date(now.getTime() - i * 3600000);
      const point = dataMap.get(hourDate.getTime());
      result.push({
        label: hourDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false }),
        succeeded: point?.succeeded ?? 0,
        failed: point?.failed ?? 0,
      });
    }
    return result;
  }

  // Aggregate into daily totals
  const days = Math.ceil(hours / 24);
  const result = [];
  for (let d = days - 1; d >= 0; d--) {
    const dayStart = new Date(now.getTime() - d * 86400000);
    dayStart.setHours(0, 0, 0, 0);
    let succeeded = 0;
    let failed = 0;
    for (let h = 0; h < 24; h++) {
      const hourKey = new Date(dayStart.getTime() + h * 3600000).getTime();
      const point = dataMap.get(hourKey);
      if (point) {
        succeeded += point.succeeded;
        failed += point.failed;
      }
    }
    result.push({
      label: `${dayStart.toLocaleDateString([], { weekday: 'short' })} ${String(dayStart.getDate()).padStart(2, '0')}.${String(dayStart.getMonth() + 1).padStart(2, '0')}`,
      succeeded,
      failed,
    });
  }
  return result;
}

export default function DashboardPage() {
  const { stats } = useDashboardStore();

  // Historical graph — hourly data
  const [historyHours, setHistoryHours] = useState(24);
  const historyQuery = useStatsHistory(historyHours);
  const history: StatsHistoryPoint[] = useMemo(() => historyQuery.data ?? [], [historyQuery.data]);

  // Sparkline source: last 24h, served from cache when historyHours === 24.
  // TODO: no per-state historical endpoint exists for Enqueued/Processing/Scheduled/
  // Messages/Batches — those cards render an empty 32px placeholder until a
  // per-state series is added to the backend (or sampled from realtime store history).
  const sparkSourceQuery = useStatsHistory(24);
  const sparkSource: StatsHistoryPoint[] = useMemo(
    () => sparkSourceQuery.data ?? [],
    [sparkSourceQuery.data],
  );

  const succeededSpark = useMemo(() => sparkSource.map(p => p.succeeded), [sparkSource]);
  const failedSpark = useMemo(() => sparkSource.map(p => p.failed), [sparkSource]);
  const totals = useMemo(() => {
    const s = sparkSource.reduce((a, p) => a + p.succeeded, 0);
    const f = sparkSource.reduce((a, p) => a + p.failed, 0);
    const denom = s + f;
    return {
      succeeded: s,
      failed: f,
      failurePct: denom > 0 ? Math.round((f / denom) * 100) : null,
      completedPct: denom > 0 ? Math.round((s / denom) * 100) : null,
    };
  }, [sparkSource]);

  if (!stats) {
    return <DashboardSkeleton />;
  }

  return (
    <div>
      <h1 className="text-2xl font-bold mb-6">Dashboard</h1>
      {/* Live counts */}
      <div className="grid grid-cols-3 lg:grid-cols-6 gap-4 mb-8">
        <MetricCard
          label="Enqueued"
          value={stats.created}
          icon={<Briefcase className="h-5 w-5" />}
          href="/jobs/enqueued"
          // TODO: no per-state history endpoint — empty placeholder for layout stability.
          sparkline={[]}
        />
        <MetricCard
          label="Processing"
          value={stats.processing}
          icon={<Loader className="h-5 w-5" />}
          color={stats.processing > 0 ? 'text-purple-600' : undefined}
          href="/jobs/processing"
          sparkline={[]}
        />
        <MetricCard
          label="Scheduled"
          value={stats.scheduled}
          icon={<Clock className="h-5 w-5" />}
          href="/jobs/scheduled"
          sparkline={[]}
        />
        <MetricCard
          label="Failed"
          value={stats.failed}
          icon={<XCircle className="h-5 w-5" />}
          color={stats.failed > 0 ? 'text-red-600' : undefined}
          href="/jobs/failed"
          sparkline={failedSpark}
          sparklineColor="#ef4444"
          subtitle={totals.failurePct != null ? `${totals.failurePct}% failure rate (24h)` : undefined}
        />
        <MetricCard
          label="Messages"
          value={stats.messages}
          icon={<Mail className="h-5 w-5" />}
          href="/messages"
          sparkline={[]}
        />
        <MetricCard
          label="Batches"
          value={stats.batchesProcessing}
          icon={<Layers className="h-5 w-5" />}
          href="/batches/processing"
          sparkline={[]}
        />
      </div>

      {/* Completed-vs-Failed summary card */}
      {totals.completedPct != null && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-8">
          <MetricCard
            label="Completed (24h)"
            value={totals.succeeded}
            icon={<Briefcase className="h-5 w-5" />}
            href="/jobs/completed"
            sparkline={succeededSpark}
            sparklineColor="#22c55e"
            subtitle={`${totals.completedPct}% of finished jobs`}
          />
          <MetricCard
            label="Failed (24h)"
            value={totals.failed}
            icon={<XCircle className="h-5 w-5" />}
            color={totals.failed > 0 ? 'text-red-600' : undefined}
            href="/jobs/failed"
            sparkline={failedSpark}
            sparklineColor="#ef4444"
            subtitle={`${totals.failurePct}% of finished jobs`}
          />
        </div>
      )}

      {/* Realtime Graph */}
      <Card className="mb-8">
        <CardHeader className="pb-2"><CardTitle className="text-sm">Realtime</CardTitle></CardHeader>
        <CardContent>
          <RealtimeChart height={200} />
        </CardContent>
      </Card>

      {/* Historical Graph */}
      <Card>
        <CardHeader className="pb-2">
          <div className="flex items-center justify-between">
            <CardTitle className="text-sm">History</CardTitle>
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
          </div>
        </CardHeader>
        <CardContent>
          <HistoryChart data={padHistory(history, historyHours)} />
        </CardContent>
      </Card>
    </div>
  );
}

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, ChartTooltip, Legend);

function HistoryChart({ data }: { data: { label: string; succeeded: number; failed: number }[] }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const chartRef = useRef<Chart | null>(null);

  // Create chart once
  useEffect(() => {
    if (!canvasRef.current) return;

    const isDark = document.documentElement.classList.contains('dark');
    const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.08)';
    const textColor = isDark ? '#888' : '#666';

    chartRef.current = new Chart(canvasRef.current, {
      type: 'line',
      data: { labels: [], datasets: [
        { label: 'Succeeded', data: [], borderColor: '#22c55e', backgroundColor: 'rgba(34, 197, 94, 0.15)', borderWidth: 2, fill: true, pointRadius: 0, pointHitRadius: 10, tension: 0.3 },
        { label: 'Failed', data: [], borderColor: '#ef4444', backgroundColor: 'rgba(239, 68, 68, 0.15)', borderWidth: 2, fill: true, pointRadius: 0, pointHitRadius: 10, tension: 0.3 },
      ]},
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
          legend: { display: false },
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

  // Update data in-place
  useEffect(() => {
    if (!chartRef.current) return;
    chartRef.current.data.labels = data.map(d => d.label);
    chartRef.current.data.datasets[0].data = data.map(d => d.succeeded);
    chartRef.current.data.datasets[1].data = data.map(d => d.failed);
    chartRef.current.update();
  }, [data]);

  return (
    <div style={{ height: 200 }}>
      <canvas ref={canvasRef} />
    </div>
  );
}
