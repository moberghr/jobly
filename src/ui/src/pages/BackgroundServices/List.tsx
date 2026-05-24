import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { LoadingState, ErrorState } from '@/components/PageState';
import { ServiceScope } from '@/types/backgroundServices';
import type { BackgroundServiceListItem } from '@/types/backgroundServices';
import * as api from '@/api';

export default function BackgroundServicesList() {
  const navigate = useNavigate();
  const [items, setItems] = useState<BackgroundServiceListItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    try {
      const list = await api.getBackgroundServices();
      setItems(list);
      setError(null);
    } catch {
      setError('Unable to load background services');
    }
  }, []);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 2000);

    return () => clearInterval(interval);
  }, [fetchData]);

  if (error) return <ErrorState message={error} />;
  if (!items) return <LoadingState />;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Background Services</h1>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Scope</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Restarts</TableHead>
              <TableHead>Last Error</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center text-muted-foreground py-8">
                  No background services registered
                </TableCell>
              </TableRow>
            ) : (
              items.map((item) => (
                <TableRow
                  key={item.name}
                  className="cursor-pointer hover:bg-accent/50"
                  onClick={() => navigate(`/services/${encodeURIComponent(item.name)}`)}
                >
                  <TableCell className="font-medium">
                    <span className="flex items-center gap-2">
                      {item.name}
                      {item.configurationMismatchCount > 0 && (
                        <span
                          className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400"
                          title={`${item.configurationMismatchCount} instance(s) have a configuration mismatch`}
                        >
                          Mismatch
                        </span>
                      )}
                    </span>
                  </TableCell>
                  <TableCell>
                    <ScopeBadge scope={item.scope} />
                  </TableCell>
                  <TableCell>
                    <StatusSummary item={item} />
                  </TableCell>
                  <TableCell className="tabular-nums">
                    {item.totalRestartCount > 0 ? (
                      <span className="text-amber-600 dark:text-amber-400">{item.totalRestartCount}</span>
                    ) : (
                      <span className="text-muted-foreground">0</span>
                    )}
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {item.lastErrorType ? (
                      <span className="text-red-600 dark:text-red-400 font-mono text-xs">{shortTypeName(item.lastErrorType)}</span>
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}

function ScopeBadge({ scope }: { scope: number }) {
  const label = scope === ServiceScope.Singleton ? 'Singleton' : 'Per Server';
  const cls =
    scope === ServiceScope.Singleton
      ? 'bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-400'
      : 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-400';

  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${cls}`}>
      {label}
    </span>
  );
}

function StatusSummary({ item }: { item: BackgroundServiceListItem }) {
  const parts: string[] = [];

  if (item.scope === ServiceScope.PerServer) {
    parts.push(`Running ${item.runningCount}/${item.totalInstances}`);
  } else {
    if (item.runningCount > 0) {
      parts.push(`Running on ${item.runningCount}`);
    }
    if (item.waitingCount > 0) {
      parts.push(`Waiting ${item.waitingCount}`);
    }
    if (item.runningCount === 0 && item.waitingCount === 0) {
      parts.push(`${item.totalInstances} instances`);
    }
  }

  const hasFault = item.faultedCount > 0;

  return (
    <span className="flex items-center gap-1.5 flex-wrap">
      <span className="text-sm">{parts.join(', ')}</span>
      {hasFault && (
        <span className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400">
          {item.faultedCount} faulted
        </span>
      )}
      {item.configurationMismatchCount > 0 && (
        <span className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-400">
          {item.configurationMismatchCount} mismatch
        </span>
      )}
    </span>
  );
}

function shortTypeName(fullName: string): string {
  const withoutAssembly = fullName.split(',')[0].trim();

  return withoutAssembly.split('.').pop() ?? fullName;
}
