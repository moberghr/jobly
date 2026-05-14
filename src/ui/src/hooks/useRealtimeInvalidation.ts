import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { subscribeRealtime } from '@/lib/realtimeBus';
import { queryScopes } from '@/lib/queryClient';

/**
 * Single mounted bridge between the realtime event bus and React Query.
 *
 * Replaces the per-page `useRealtimeRefetch` pattern. Mounted once in
 * `MainLayout`; routes hub events to broad query invalidations so every
 * mounted page picks up fresh data without re-implementing the bridge.
 *
 * Counters are also invalidated on `JobFinalized` because the dashboard's
 * succeeded/failed counters update on every completion.
 */
export function useRealtimeInvalidation() {
  const qc = useQueryClient();

  useEffect(() => {
    const onJobFinalized = () => {
      qc.invalidateQueries({ queryKey: queryScopes.jobs });
      qc.invalidateQueries({ queryKey: queryScopes.detail });
      qc.invalidateQueries({ queryKey: queryScopes.counters });
      qc.invalidateQueries({ queryKey: queryScopes.stats });
      qc.invalidateQueries({ queryKey: queryScopes.dashboard });
      qc.invalidateQueries({ queryKey: queryScopes.messages });
      qc.invalidateQueries({ queryKey: queryScopes.batches });
    };

    const onMessageEnqueued = () => {
      qc.invalidateQueries({ queryKey: queryScopes.messages });
      qc.invalidateQueries({ queryKey: queryScopes.jobs });
      qc.invalidateQueries({ queryKey: queryScopes.dashboard });
    };

    const unsubJob = subscribeRealtime('JobFinalized', onJobFinalized);
    const unsubMsg = subscribeRealtime('MessageEnqueued', onMessageEnqueued);

    return () => {
      unsubJob();
      unsubMsg();
    };
  }, [qc]);
}
