import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import * as api from '@/api';
import { queryKeys, queryScopes } from '@/lib/queryClient';

export function useServers() {
  return useQuery({
    queryKey: queryKeys.servers,
    queryFn: () => api.getServers(),
    refetchInterval: 10_000,
  });
}

export function useServerDetail(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.serverDetail(id ?? ''),
    queryFn: () => api.getServerById(id!),
    enabled: !!id,
    refetchInterval: 10_000,
  });
}

export function useServerTasks(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.serverTasks(id ?? ''),
    queryFn: () => api.getServerTaskSummaries(id!),
    enabled: !!id,
    refetchInterval: 10_000,
  });
}

export function useServerLogs(
  id: string | undefined,
  page: number,
  pageSize: number,
  taskName: string | undefined,
  enabled: boolean,
) {
  return useQuery({
    queryKey: queryKeys.serverLogs(id ?? '', page, pageSize, taskName),
    queryFn: () => api.getServerLogs(id!, page, pageSize, taskName),
    enabled: !!id && enabled,
  });
}

export function useWorkerDetail(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.workerDetail(id ?? ''),
    queryFn: () => api.getWorkerById(id!),
    enabled: !!id,
  });
}

export function useWorkerLogs(id: string | undefined, page: number, pageSize: number) {
  return useQuery({
    queryKey: queryKeys.workerLogs(id ?? '', page, pageSize),
    queryFn: () => api.getWorkerJobLogs(id!, page, pageSize),
    enabled: !!id,
  });
}

function invalidateServers(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: queryScopes.servers });
}

export function usePauseServer() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => api.pauseServer(id),
    onSuccess: () => {
      invalidateServers(qc);
      toast.success('Server paused');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}

export function useResumeServer() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => api.resumeServer(id),
    onSuccess: () => {
      invalidateServers(qc);
      toast.success('Server resumed');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}

export function usePauseWorkerGroup() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => api.pauseWorkerGroup(id),
    onSuccess: () => {
      invalidateServers(qc);
      toast.success('Worker group paused');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}

export function useResumeWorkerGroup() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => api.resumeWorkerGroup(id),
    onSuccess: () => {
      invalidateServers(qc);
      toast.success('Worker group resumed');
    },
    onError: (err: Error) => toast.error(err.message),
  });
}
