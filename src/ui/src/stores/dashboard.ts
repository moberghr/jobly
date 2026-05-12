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

const WINDOW_SIZE = 60;

export const useDashboardStore = create<DashboardStore>((set, get) => {
  // Tracks the totalSucceeded/totalFailed values at the last *rate sample*.
  // Decoupled from fetchStats — that runs event-driven, possibly many times per
  // second, and using it for rate-delta would produce wildly varying y-values.
  let prevSampled: { succeeded: number; failed: number } | null = null;

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
      }
    },
    sampleRate: () => {
      const stats = get().stats;
      if (!stats) return;

      const current = { succeeded: stats.totalSucceeded, failed: stats.totalFailed };
      const prev = prevSampled;
      prevSampled = current;

      if (!prev) return; // first sample establishes the baseline only

      const newPoint: RealtimePoint = {
        ts: Math.floor(Date.now() / 1000),
        succeeded: Math.max(0, current.succeeded - prev.succeeded),
        failed: Math.max(0, current.failed - prev.failed),
      };

      set((state) => ({
        realtimeData: [...state.realtimeData, newPoint].slice(-WINDOW_SIZE),
      }));
    },
  };
});
