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
}

const WINDOW_SIZE = 30;

export const useDashboardStore = create<DashboardStore>((set) => {
  let prevTotals: { succeeded: number; failed: number } | null = null;

  return {
    stats: null,
    loading: false,
    error: null,
    realtimeData: [],
    fetchStats: async () => {
      try {
        const stats = await getStatus();

        const current = { succeeded: stats.totalSucceeded, failed: stats.totalFailed };
        let newPoint: RealtimePoint | null = null;

        if (prevTotals) {
          newPoint = {
            ts: Math.floor(Date.now() / 1000),
            succeeded: current.succeeded - prevTotals.succeeded,
            failed: current.failed - prevTotals.failed,
          };
        }

        prevTotals = current;

        set((state) => ({
          stats,
          loading: false,
          error: null,
          realtimeData: newPoint
            ? [...state.realtimeData, newPoint].slice(-WINDOW_SIZE)
            : state.realtimeData,
        }));
      } catch {
        set({ loading: false, error: 'Unable to connect to Jobly API' });
      }
    },
  };
});
