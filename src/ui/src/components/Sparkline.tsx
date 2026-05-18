import { useMemo } from 'react';
import { Line } from 'react-chartjs-2';
import { Chart, LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler } from 'chart.js';

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler);

interface SparklineProps {
  data: number[];
  color?: string;
  height?: number;
}

// Tiny line chart — no axes, no legend, no tooltip. ~32px tall by default.
// Renders an empty placeholder when data is empty to prevent layout shift.
export function Sparkline({ data, color = 'hsl(var(--primary))', height = 32 }: SparklineProps) {
  const chartData = useMemo(
    () => ({
      labels: data.map((_, i) => i.toString()),
      datasets: [
        {
          data,
          borderColor: color,
          backgroundColor: `${color}33`,
          borderWidth: 1.5,
          fill: true,
          pointRadius: 0,
          tension: 0.35,
        },
      ],
    }),
    [data, color],
  );

  if (data.length === 0) {
    return <div style={{ height }} aria-hidden="true" />;
  }

  return (
    <div style={{ height }} aria-hidden="true">
      <Line
        data={chartData}
        options={{
          responsive: true,
          maintainAspectRatio: false,
          animation: false,
          events: [],
          scales: {
            x: { display: false },
            y: { display: false, beginAtZero: true },
          },
          plugins: {
            legend: { display: false },
            tooltip: { enabled: false },
          },
          elements: { line: { borderJoinStyle: 'round' } },
        }}
      />
    </div>
  );
}
