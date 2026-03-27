import { create } from 'zustand';
import type { DashboardStatistics } from '@/types';
import { getStatus } from '@/api';

interface DashboardStore {
  stats: DashboardStatistics | null;
  loading: boolean;
  fetchStats: () => Promise<void>;
}

export const useDashboardStore = create<DashboardStore>((set) => ({
  stats: null,
  loading: false,
  fetchStats: async () => {
    set({ loading: true });
    try {
      const stats = await getStatus();
      set({ stats, loading: false });
    } catch {
      set({ loading: false });
    }
  },
}));
