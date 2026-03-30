import { useRef, useEffect } from 'react';
import {
  Chart,
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  Filler,
  Tooltip,
  Legend,
  CategoryScale,
} from 'chart.js';
import 'chartjs-adapter-luxon';
import StreamingPlugin, { RealTimeScale } from 'chartjs-plugin-streaming';
import { useDashboardStore } from '@/stores/dashboard';

Chart.register(
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  CategoryScale,
  Filler,
  Tooltip,
  Legend,
  RealTimeScale,
  StreamingPlugin,
);

interface RealtimeChartProps {
  height?: number;
}

export function RealtimeChart({ height = 200 }: RealtimeChartProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const chartRef = useRef<Chart | null>(null);
  const renderedCount = useRef(0);
  const realtimeData = useDashboardStore((s) => s.realtimeData);

  const vals = realtimeData.map((p) => p.succeeded + p.failed);
  const current = vals.length > 0 ? vals[vals.length - 1] : 0;
  const max = vals.length > 0 ? Math.max(...vals) : 0;
  const avg = vals.length >= 5 ? Math.round(vals.reduce((a, b) => a + b, 0) / vals.length) : null;

  useEffect(() => {
    if (!canvasRef.current) {
      return;
    }

    const isDark = document.documentElement.classList.contains('dark');
    const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.08)';
    const textColor = isDark ? '#888' : '#666';

    const storeData = useDashboardStore.getState().realtimeData;
    renderedCount.current = storeData.length;

    const chart = new Chart(canvasRef.current, {
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
            pointHitRadius: 0,
            pointHoverRadius: 0,
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
            pointHitRadius: 0,
            pointHoverRadius: 0,
            tension: 0.3,
            data: storeData.map((p) => ({ x: p.ts * 1000, y: p.failed })),
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        interaction: { mode: 'nearest', axis: 'x', intersect: false },
        hover: { mode: undefined },
        scales: {
          x: {
            type: 'realtime',
            realtime: {
              duration: 60000,
              refresh: 1000,
              delay: 1000,
              onRefresh: (c) => {
                const data = useDashboardStore.getState().realtimeData;
                const now = Date.now();
                for (let i = renderedCount.current; i < data.length; i++) {
                  const p = data[i];
                  c.data.datasets[0].data.push({ x: now, y: p.succeeded });
                  c.data.datasets[1].data.push({ x: now, y: p.failed });
                }
                renderedCount.current = data.length;
              },
            },
            time: { displayFormats: { second: 'HH:mm:ss', minute: 'HH:mm', hour: 'HH:mm' } },
            ticks: { color: textColor, font: { size: 10 } },
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

    chartRef.current = chart;

    return () => {
      chart.destroy();
      chartRef.current = null;
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div>
      <div className="flex gap-6 mb-2 text-sm">
        <span className="text-muted-foreground">Current: <span className="font-medium text-foreground">{current}/s</span></span>
        <span className="text-muted-foreground">Avg: <span className="font-medium text-foreground">{avg != null ? `${avg}/s` : '-'}</span></span>
        <span className="text-muted-foreground">Peak: <span className="font-medium text-foreground">{max}/s</span></span>
      </div>
      <div style={{ height }}>
        <canvas ref={canvasRef} />
      </div>
    </div>
  );
}
