import { useEffect, useMemo, useRef, useState } from 'react';
import {
  createChart,
  AreaSeries,
  LineSeries,
  type IChartApi,
  type ISeriesApi,
  type UTCTimestamp,
} from 'lightweight-charts';
import { Panel } from '@/components/v2/Panel';
import { PulseDot } from '@/components/v2/PulseDot';
import { useDashboardStore } from '@/stores/dashboard';
import { ema } from '@/lib/svgPath';

type Range = '1m' | '5m' | '15m' | '1h';

const RANGE_SECONDS: Record<Range, number> = {
  '1m': 60,
  '5m': 300,
  '15m': 900,
  '1h': 3600,
};

// Rate-sampler frequency. 5Hz (200ms) gives a much smoother chart than 1Hz
// because the EMA has 5× more data points to average. The store buffer
// (WINDOW_SIZE in stores/dashboard.ts) is sized for this rate.
const SAMPLE_HZ = 5;
const SAMPLE_INTERVAL_MS = 1000 / SAMPLE_HZ;

// EMA smoothing factor. Source-side already produces 1-second moving-avg
// rates (see stores/dashboard.ts), so EMA is light here — just enough to
// polish the visual without making the line cling after activity stops.
// Effective window ≈ 1/α samples × 200ms (1m: ~1.5s, 5m: ~3s).
const EMA_ALPHA: Record<Range, number> = {
  '1m': 0.15,
  '5m': 0.07,
  '15m': 0.04,
  '1h': 0.02,
};

const H = 220;

export function ThroughputChart() {
  const [range, setRange] = useState<Range>('1m');
  const realtimeData = useDashboardStore((s) => s.realtimeData);

  // High-frequency rate sampler.
  useEffect(() => {
    const id = window.setInterval(() => {
      useDashboardStore.getState().sampleRate();
    }, SAMPLE_INTERVAL_MS);

    return () => window.clearInterval(id);
  }, []);

  const windowSec = RANGE_SECONDS[range];
  const alpha = EMA_ALPHA[range];

  // Header metrics: take the last second of samples to compute "now" (avoids
  // single-sample noise), and slice by samples-per-second for window math.
  const windowSamples = windowSec * SAMPLE_HZ;
  const visibleSucc = useMemo(
    () => realtimeData.slice(-windowSamples).map((p) => p.succeeded),
    [realtimeData, windowSamples],
  );
  const lastSecond = visibleSucc.slice(-SAMPLE_HZ);
  const now = lastSecond.length
    ? Math.round(lastSecond.reduce((a, b) => a + b, 0) / lastSecond.length)
    : 0;
  const peak = visibleSucc.length ? Math.round(Math.max(...visibleSucc)) : 0;
  const avg = visibleSucc.length >= SAMPLE_HZ * 5
    ? Math.round(visibleSucc.reduce((a, b) => a + b, 0) / visibleSucc.length)
    : null;

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
        <TVChart data={realtimeData} windowSec={windowSec} alpha={alpha} />
        {realtimeData.length < 2 && (
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
            <span className="mono text-[11px] text-text-mute">collecting samples…</span>
          </div>
        )}
      </div>
    </Panel>
  );
}

interface TVChartProps {
  data: { ts: number; succeeded: number; failed: number }[];
  windowSec: number;
  alpha: number;
}

/**
 * TradingView lightweight-charts implementation. Built for live financial
 * time-series — produces buttery-smooth time-axis scrolling and animated
 * series updates out of the box. Replaces the hand-rolled SVG attempt.
 */
function TVChart({ data, windowSec, alpha }: TVChartProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const succRef = useRef<ISeriesApi<'Area'> | null>(null);
  const failRef = useRef<ISeriesApi<'Line'> | null>(null);

  // Refs for the rAF loop so it reads fresh values without remounting the chart.
  const dataRef = useRef(data);
  const windowRef = useRef(windowSec);
  const alphaRef = useRef(alpha);
  dataRef.current = data;
  windowRef.current = windowSec;
  alphaRef.current = alpha;

  // Mount the chart once.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    const css = getComputedStyle(document.documentElement);
    const green = css.getPropertyValue('--warp-green').trim() || '#22c55e';
    const red = css.getPropertyValue('--warp-red').trim() || '#ef4444';
    const muted = css.getPropertyValue('--text-mute').trim() || '#71717a';
    const border = css.getPropertyValue('--border').trim() || '#e5e7eb';

    const chart = createChart(container, {
      width: container.clientWidth,
      height: H,
      layout: {
        background: { color: 'transparent' },
        textColor: muted,
        fontSize: 10,
        fontFamily: '"Geist Mono Variable", ui-monospace, monospace',
      },
      grid: {
        vertLines: { visible: false },
        horzLines: { color: border, style: 1 },
      },
      rightPriceScale: {
        borderVisible: false,
        // bottom: 0 keeps the zero line flush with the chart bottom and
        // prevents any negative tick labels from appearing below the data
        // (autoscaleInfoProvider on each series floors the data range at 0;
        // the margin is what was leaking into negative visual space).
        scaleMargins: { top: 0.1, bottom: 0 },
      },
      timeScale: {
        borderVisible: false,
        timeVisible: true,
        secondsVisible: true,
        rightOffset: 0,
        barSpacing: 1,
      },
      crosshair: {
        mode: 0,
        vertLine: { color: muted, width: 1, style: 2, labelVisible: false },
        horzLine: { color: muted, width: 1, style: 2, labelVisible: false },
      },
      handleScale: false,
      handleScroll: false,
      autoSize: false,
    });

    // Force the auto-range floor at 0 so the y-axis never shows negative
    // ticks when the series sits at zero (auto-scale otherwise pads the
    // bottom margin into negative territory).
    //
    // When `original()` returns null (no data yet on cold start), lightweight-
    // charts falls back to a default range that can dip negative. Return a
    // sane [0, 10] range so the empty chart renders cleanly above zero.
    const floorAtZero = (original: () => { priceRange: { minValue: number; maxValue: number } } | null) => {
      const r = original();
      if (r) {
        r.priceRange.minValue = 0;

        return r;
      }

      return { priceRange: { minValue: 0, maxValue: 10 } };
    };

    const succ = chart.addSeries(AreaSeries, {
      lineColor: green,
      topColor: withAlpha(green, 0.42),
      bottomColor: withAlpha(green, 0),
      lineWidth: 2,
      priceLineVisible: false,
      lastValueVisible: false,
      crosshairMarkerVisible: false,
      autoscaleInfoProvider: floorAtZero,
    });
    const fail = chart.addSeries(LineSeries, {
      color: red,
      lineWidth: 2,
      priceLineVisible: false,
      lastValueVisible: false,
      crosshairMarkerVisible: false,
      autoscaleInfoProvider: floorAtZero,
    });

    chartRef.current = chart;
    succRef.current = succ;
    failRef.current = fail;

    const ro = new ResizeObserver(() => {
      const w = container.clientWidth;
      if (w > 0 && chartRef.current) {
        chartRef.current.applyOptions({ width: w });
      }
    });
    ro.observe(container);

    // rAF loop: push EMA-smoothed data and pan the time scale to a rolling
    // window ending at `now`. Lightweight-charts interpolates point positions
    // between frames, giving a flowing live-data feel.
    let raf = 0;
    let lastDataLen = -1;
    const tick = () => {
      const d = dataRef.current;
      if (d.length >= 2) {
        // Only rebuild the dataset when the source array length changed
        // (1Hz). The rAF loop still re-pans the visible range every frame
        // for smooth scrolling between samples.
        if (d.length !== lastDataLen) {
          lastDataLen = d.length;
          const succValues = ema(d.map((p) => p.succeeded), alphaRef.current);
          const failValues = ema(d.map((p) => p.failed), alphaRef.current);
          // Lightweight-charts requires strictly-ascending unique timestamps.
          // `realtimeData` is built that way already (1Hz Unix seconds).
          const succData = d.map((p, i) => ({
            time: p.ts as UTCTimestamp,
            value: Math.max(0, succValues[i]),
          }));
          const failData = d.map((p, i) => ({
            time: p.ts as UTCTimestamp,
            value: Math.max(0, failValues[i]),
          }));
          succRef.current?.setData(succData);
          failRef.current?.setData(failData);
        }

        const nowSec = Math.floor(Date.now() / 1000) + (Date.now() % 1000) / 1000;
        const fromSec = nowSec - windowRef.current;
        chartRef.current?.timeScale().setVisibleRange({
          from: fromSec as UTCTimestamp,
          to: nowSec as UTCTimestamp,
        });
      }
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);

    return () => {
      cancelAnimationFrame(raf);
      ro.disconnect();
      chart.remove();
      chartRef.current = null;
      succRef.current = null;
      failRef.current = null;
    };
  }, []);

  return <div ref={containerRef} className="h-full w-full" style={{ height: H }} />;
}

/** Returns a CSS color with the given alpha. Handles hex / falls back to color-mix. */
function withAlpha(color: string, alpha: number): string {
  const trimmed = color.trim();
  if (trimmed.startsWith('#')) {
    const hex = trimmed.slice(1);
    if (hex.length === 6) {
      const r = parseInt(hex.slice(0, 2), 16);
      const g = parseInt(hex.slice(2, 4), 16);
      const b = parseInt(hex.slice(4, 6), 16);
      return `rgba(${r},${g},${b},${alpha})`;
    }
  }

  return `color-mix(in srgb, ${trimmed} ${Math.round(alpha * 100)}%, transparent)`;
}
