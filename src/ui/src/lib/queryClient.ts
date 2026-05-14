import { QueryClient } from '@tanstack/react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5_000,
      gcTime: 5 * 60_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error: unknown) => {
        const status = (error as { response?: { status?: number } })?.response?.status;
        if (status === 401 || status === 403 || status === 404) {
          return false;
        }

        return failureCount < 2;
      },
    },
    mutations: {
      retry: false,
    },
  },
});

export const queryKeys = {
  dashboardStatus: ['dashboard', 'status'] as const,
  dashboardStats: (range?: string) => ['dashboard', 'stats', range ?? '24h'] as const,
  jobs: (state: string, page: number) => ['jobs', state, page] as const,
  job: (id: string) => ['job', id] as const,
  jobLogs: (id: string) => ['job', id, 'logs'] as const,
  messages: (state: string, page: number) => ['messages', state, page] as const,
  batches: (state: string, page: number) => ['batches', state, page] as const,
  recurring: ['recurring'] as const,
  recurringDetail: (id: string) => ['recurring', id] as const,
  servers: ['servers'] as const,
  serverDetail: (id: string) => ['servers', id] as const,
  workerDetail: (id: string) => ['workers', id] as const,
  counters: ['counters'] as const,
  concurrencyLimits: ['concurrency-limits'] as const,
  rateLimits: ['rate-limits'] as const,
  trace: (id: string) => ['trace', id] as const,
};
