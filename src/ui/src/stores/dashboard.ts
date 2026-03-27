import { create } from 'zustand';
import type { DashboardStatistics } from '@/types';
import { getStatus } from '@/api';

interface DashboardStore {
  stats: DashboardStatistics | null;
  loading: boolean;
  error: string | null;
  fetchStats: () => Promise<void>;
}

export const useDashboardStore = create<DashboardStore>((set) => ({
  stats: null,
  loading: false,
  error: null,
  fetchStats: async () => {
    try {
      const stats = await getStatus();
      set({ stats, loading: false, error: null });
    } catch {
      set({ loading: false, error: 'Unable to connect to Jobly API' });
    }
  },
}));
