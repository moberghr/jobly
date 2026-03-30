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
} from 'chart.js';
import 'chartjs-adapter-luxon';
import StreamingPlugin, { RealTimeScale } from 'chartjs-plugin-streaming';
import { useDashboardStore } from '@/stores/dashboard';

Chart.register(
  LineController,
  LineElement,
  PointElement,
  LinearScale,
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
  const prevTotals = useRef<{ succeeded: number; failed: number } | null>(null);

  useEffect(() => {
    if (!canvasRef.current) {
      return;
    }

    const isDark = document.documentElement.classList.contains('dark');
    const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.08)';
    const textColor = isDark ? '#888' : '#666';

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
            tension: 0.3,
            data: [],
          },
          {
            label: 'Failed/s',
            borderColor: '#ef4444',
            backgroundColor: 'rgba(239, 68, 68, 0.15)',
            borderWidth: 2,
            fill: true,
            pointRadius: 0,
            tension: 0.3,
            data: [],
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        scales: {
          x: {
            type: 'realtime',
            realtime: {
              duration: 60000,
              refresh: 1000,
              delay: 1000,
              onRefresh: (chart) => {
                const state = useDashboardStore.getState();
                if (!state.stats) {
                  return;
                }

                const current = {
                  succeeded: state.stats.totalSucceeded,
                  failed: state.stats.totalFailed,
                };
                const now = Date.now();

                if (prevTotals.current) {
                  const succeeded = current.succeeded - prevTotals.current.succeeded;
                  const failed = current.failed - prevTotals.current.failed;

                  chart.data.datasets[0].data.push({ x: now, y: succeeded });
                  chart.data.datasets[1].data.push({ x: now, y: failed });
                }

                prevTotals.current = current;
              },
            },
            time: {
              displayFormats: {
                second: 'HH:mm:ss',
                minute: 'HH:mm',
                hour: 'HH:mm',
              },
            },
            ticks: {
              color: textColor,
              font: { size: 10 },
            },
            grid: {
              color: gridColor,
            },
          },
          y: {
            beginAtZero: true,
            ticks: {
              color: textColor,
              font: { size: 10 },
              precision: 0,
            },
            grid: {
              color: gridColor,
            },
          },
        },
        plugins: {
          legend: {
            display: false,
          },
          tooltip: {
            enabled: true,
            mode: 'index' as const,
            intersect: false,
          },
        },
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
    <div style={{ height }}>
      <canvas ref={canvasRef} />
    </div>
  );
}
