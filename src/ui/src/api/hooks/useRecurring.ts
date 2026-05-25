import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import * as api from '@/api';
import { queryKeys, queryScopes } from '@/lib/queryClient';

export function useRecurringList(page: number, pageSize: number) {
  return useQuery({
    queryKey: queryKeys.recurring(page, pageSize),
    queryFn: () => api.getRecurringJobs(page, pageSize),
  });
}

export function useRecurringDetail(id: number | undefined) {
  return useQuery({
    queryKey: queryKeys.recurringDetail(id ?? -1),
    queryFn: () => api.getRecurringJobById(id!),
    enabled: id !== undefined && !Number.isNaN(id),
  });
}

export function useRecurringJobs(id: number | undefined, page: number, pageSize: number) {
  return useQuery({
    queryKey: queryKeys.recurringJobs(id ?? -1, page, pageSize),
    queryFn: () => api.getRecurringJobJobs(id!, page, pageSize),
    enabled: id !== undefined && !Number.isNaN(id),
  });
}

function invalidateRecurring(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: queryScopes.recurring });
}

export function useEnableRecurringJob() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: number) => api.enableRecurringJob(id),
    onSuccess: () => {
      invalidateRecurring(qc);
      toast.success('Recurring job enabled');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}

export function useDisableRecurringJob() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: number) => api.disableRecurringJob(id),
    onSuccess: () => {
      invalidateRecurring(qc);
      toast.success('Recurring job disabled');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}

export function useTriggerRecurringJob() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: number) => api.triggerRecurringJob(id),
    onSuccess: () => {
      invalidateRecurring(qc);
      qc.invalidateQueries({ queryKey: queryScopes.jobs });
      toast.success('Recurring job triggered');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}

export function useDeleteRecurringJob() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: number) => api.deleteRecurringJob(id),
    onSuccess: () => {
      invalidateRecurring(qc);
      toast.success('Recurring job deleted');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}
