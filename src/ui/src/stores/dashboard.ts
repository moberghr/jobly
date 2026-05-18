import { create } from 'zustand';
import type { DashboardStatistics } from '@/types';
import { getStatus } from '@/api';

export interface RealtimePoint {
  ts: number; // unix seconds
  succeeded: number;
  failed: number;
}

interface DashboardStore {
  stats: DashboardStatistics | null;
  loading: boolean;
  error: string | null;
  realtimeData: RealtimePoint[];
  fetchStats: () => Promise<void>;
  sampleRate: () => void;
}

// Buffer up to 1 hour of per-second samples so RealtimeChart can render any
// window between 1m and 1h without losing historical data.
const WINDOW_SIZE = 3600;

export const useDashboardStore = create<DashboardStore>((set, get) => {
  // Tracks the totalSucceeded/totalFailed values at the last *rate sample*.
  // Decoupled from fetchStats — that runs event-driven, possibly many times per
  // second, and using it for rate-delta would produce wildly varying y-values.
  let prevSampled: { succeeded: number; failed: number } | null = null;
  let prevSampledAt: number | null = null;

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
        // Reset the rate-sample baseline so the next sample doesn't compute a
        // huge delta against stale pre-disconnect totals.
        prevSampled = null;
        prevSampledAt = null;
      }
    },
    sampleRate: () => {
      const stats = get().stats;
      if (!stats) return;

      const nowSec = Math.floor(Date.now() / 1000);
      const current = { succeeded: stats.totalSucceeded, failed: stats.totalFailed };
      const prev = prevSampled;
      const lastAt = prevSampledAt;
      prevSampled = current;
      prevSampledAt = nowSec;

      // Re-establish the baseline if this is the first sample or if the gap
      // since the last sample is larger than the 1 Hz cadence allows. The
      // sampler runs in MainLayout so it stays alive across page navigation,
      // but browsers throttle setInterval in backgrounded tabs and freeze it
      // during sleep — when the timer wakes, stats.totalSucceeded has grown
      // and a single delta against the stale baseline would land as one
      // instant spike. Also drop the accumulated window so the old points
      // don't smooth-curve into the next real sample.
      if (!prev || lastAt === null || nowSec - lastAt > 2) {
        if (lastAt !== null && nowSec - lastAt > 2) {
          set({ realtimeData: [] });
        }
        return;
      }

      const newPoint: RealtimePoint = {
        ts: nowSec,
        succeeded: Math.max(0, current.succeeded - prev.succeeded),
        failed: Math.max(0, current.failed - prev.failed),
      };

      set((state) => ({
        realtimeData: [...state.realtimeData, newPoint].slice(-WINDOW_SIZE),
      }));
    },
  };
});
