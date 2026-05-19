import { useQuery } from '@tanstack/react-query';
import * as api from '@/api';
import { queryKeys } from '@/lib/queryClient';

export function useDashboardStatus() {
  return useQuery({
    queryKey: queryKeys.dashboardStatus,
    queryFn: () => api.getStatus(),
    refetchInterval: 30_000,
  });
}

export function useStatsHistory(hours: number) {
  return useQuery({
    queryKey: queryKeys.statsHistory(hours),
    queryFn: () => api.getStatsHistory(hours),
    refetchInterval: 60_000,
  });
}
