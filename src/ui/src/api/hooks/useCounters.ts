import { useQuery } from '@tanstack/react-query';
import * as api from '@/api';
import { queryKeys } from '@/lib/queryClient';

export function useCounters() {
  return useQuery({
    queryKey: queryKeys.counters,
    queryFn: () => api.getCounters(),
  });
}

export function useCountersHistory(hours: number) {
  return useQuery({
    queryKey: queryKeys.countersHistory(hours),
    queryFn: () => api.getCountersHistory(hours),
  });
}
