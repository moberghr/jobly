import { useState, useEffect, useCallback } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { LoadingState, ErrorState } from '@/components/PageState';
import { useRefreshKey } from '@/hooks/useRefreshKey';
import type { CounterModel } from '@/types';
import * as api from '@/api';

export default function CountersPage() {
  const [counters, setCounters] = useState<CounterModel[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const refreshKey = useRefreshKey();

  const fetchCounters = useCallback(() => {
    api.getCounters().then(setCounters).catch(() => setError('Unable to load counters'));
  }, []);

  useEffect(() => {
    fetchCounters();
    const id = setInterval(fetchCounters, 5000);
    return () => clearInterval(id);
  }, [refreshKey, fetchCounters]);

  if (error) return <ErrorState message={error} />;
  if (!counters) return <LoadingState />;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-2">Counters</h1>
      <p className="text-sm text-muted-foreground mb-4">
        Raw counter rows from the database. Built-in: <code>stats:succeeded</code>,{' '}
        <code>stats:failed</code>, <code>stats:deleted</code>, <code>stats:requeued</code>.
        Addons can write their own keys here.
      </p>

      {counters.length === 0 ? (
        <Card>
          <CardContent className="py-8 text-center text-muted-foreground">
            No counters
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="p-0">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50">
                <tr>
                  <th className="text-left font-semibold px-4 py-2">Key</th>
                  <th className="text-right font-semibold px-4 py-2 w-40">Value</th>
                </tr>
              </thead>
              <tbody>
                {counters.map((c) => (
                  <tr key={c.key} className="border-b last:border-b-0 hover:bg-muted/30">
                    <td className="px-4 py-2 font-mono">{c.key}</td>
                    <td className="px-4 py-2 text-right font-mono">{c.value.toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
