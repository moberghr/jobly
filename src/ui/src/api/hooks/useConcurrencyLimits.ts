import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import * as api from '@/api';
import { queryKeys } from '@/lib/queryClient';

export function useConcurrencyLimits() {
  return useQuery({
    queryKey: queryKeys.concurrencyLimits,
    queryFn: () => api.listConcurrencyLimits(),
    refetchInterval: 5_000,
  });
}

export function useUpsertConcurrencyLimit() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: ({ name, limit }: { name: string; limit: number }) =>
      api.upsertConcurrencyLimit(name, limit),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.concurrencyLimits });
      toast.success('Concurrency limit saved');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}

export function useDeleteConcurrencyLimit() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (name: string) => api.deleteConcurrencyLimit(name),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.concurrencyLimits });
      toast.success('Concurrency limit deleted');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}
