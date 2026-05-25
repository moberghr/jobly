import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import * as api from '@/api';
import { queryKeys } from '@/lib/queryClient';

export function useRateLimits() {
  return useQuery({
    queryKey: queryKeys.rateLimits,
    queryFn: () => api.listRateLimits(),
    refetchInterval: 5_000,
  });
}

export function useUpsertRateLimit() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: ({ name, count, windowSeconds }: { name: string; count: number; windowSeconds: number }) =>
      api.upsertRateLimit(name, count, windowSeconds),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.rateLimits });
      toast.success('Rate limit saved');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}

export function useDeleteRateLimit() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (name: string) => api.deleteRateLimit(name),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.rateLimits });
      toast.success('Rate limit deleted');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}
