import { create } from 'zustand';
import type { DashboardStatistics } from '@/types';
import { getStatus } from '@/api';

export interface RealtimePoint {
  ts: number; // unix seconds (fractional, sub-second precision)
  succeeded: number; // jobs/second rate
  failed: number; // jobs/second rate
}

interface DashboardStore {
  stats: DashboardStatistics | null;
  loading: boolean;
  error: string | null;
  realtimeData: RealtimePoint[];
  fetchStats: () => Promise<void>;
  sampleRate: () => void;
}

// Rate-sampler runs at SAMPLE_HZ (see ThroughputChart). Buffer sized to the
// longest supported chart window (1h) so the user can scroll back through a
// full hour of throughput history. At 5Hz × 3600s = 18,000 samples ≈ 360 KB.
const SAMPLE_HZ = 5;
const BUFFER_SECONDS = 3600;
const WINDOW_SIZE = BUFFER_SECONDS * SAMPLE_HZ;

// Sliding window used to compute throughput at the source: each emitted
// RealtimePoint is `(current.total - sliding_oldest.total) / elapsed`, i.e.
// a rolling moving-average rate. 500ms window keeps the source responsive
// while still absorbing single-burst quantization noise.
const RATE_WINDOW_MS = 500;

export const useDashboardStore = create<DashboardStore>((set, get) => {
  // Sliding window of recent stats snapshots. Each sample emits the rate
  // measured over the oldest snapshot still within RATE_WINDOW_MS — i.e. a
  // continuous moving-average rate that absorbs Warp's bursty event arrivals
  // before they reach the chart. Decoupled from fetchStats (which runs
  // event-driven and produces no clean rate signal on its own).
  type Snap = { ts: number; succeeded: number; failed: number };
  const snaps: Snap[] = [];
  let lastEmitMs: number | null = null;

  return {
    stats: null,
    loading: false,
    error: null,
    realtimeData: [],
    fetchStats: async () => {
      try {
        const stats = await getStatus();
        set({ stats, loading: false, error: null });
      } catch {
        set({ loading: false, error: 'Unable to connect to Warp API' });
        // Reset the sliding window so the next sample doesn't compute a huge
        // rate against stale pre-disconnect totals.
        snaps.length = 0;
        lastEmitMs = null;
      }
    },
    sampleRate: () => {
      const stats = get().stats;
      if (!stats) return;

      const nowMs = Date.now();

      // Browser throttling / tab-freeze recovery: if the sampler hasn't run
      // recently the totals have grown and the sliding window is stale. Wipe
      // both the snap window and the chart's realtime buffer so the next real
      // sample starts a fresh smooth-curve.
      if (lastEmitMs !== null && nowMs - lastEmitMs > 2000) {
        snaps.length = 0;
        set({ realtimeData: [] });
      }
      lastEmitMs = nowMs;

      // Push the current totals onto the sliding window, then trim anything
      // older than 2× RATE_WINDOW_MS (keep some headroom so we can still find
      // a valid "1 second ago" snapshot even after irregular tick spacing).
      snaps.push({ ts: nowMs, succeeded: stats.totalSucceeded, failed: stats.totalFailed });
      const cutoff = nowMs - RATE_WINDOW_MS * 2;
      while (snaps.length > 1 && snaps[0].ts < cutoff) {
        snaps.shift();
      }

      // Find the oldest snap that is still within ~1s of now. The rate is
      // (current - oldest_in_window) / elapsed → a continuous 1-second
      // moving-average. This intrinsically smooths burst-quantized event
      // arrivals at the source, independent of sample tick frequency.
      const target = nowMs - RATE_WINDOW_MS;
      let baseIdx = 0;
      for (let i = 0; i < snaps.length - 1; i++) {
        if (snaps[i].ts <= target) {
          baseIdx = i;
        } else {
          break;
        }
      }
      const base = snaps[baseIdx];
      const elapsedSec = Math.max(0.001, (nowMs - base.ts) / 1000);
      if (snaps.length < 2 || elapsedSec < 0.05) {
        // Need at least two snapshots and a non-trivial elapsed window.
        return;
      }

      const succRate = Math.max(0, stats.totalSucceeded - base.succeeded) / elapsedSec;
      const failRate = Math.max(0, stats.totalFailed - base.failed) / elapsedSec;
      const newPoint: RealtimePoint = {
        ts: nowMs / 1000,
        succeeded: succRate,
        failed: failRate,
      };

      set((state) => ({
        realtimeData: [...state.realtimeData, newPoint].slice(-WINDOW_SIZE),
      }));
    },
  };
});
