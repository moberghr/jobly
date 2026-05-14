import { useQuery } from '@tanstack/react-query';
import * as api from '@/api';
import { queryKeys } from '@/lib/queryClient';

export function useTrace(traceId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.trace(traceId ?? ''),
    queryFn: () => api.getTraceTree(traceId!),
    enabled: !!traceId,
  });
}
