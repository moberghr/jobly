import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import axios from 'axios';
import { Card, CardContent } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Pagination } from '@/components/Pagination';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import { useRefreshKey } from '@/hooks/useRefreshKey';
import { useRealtimeRefetch } from '@/hooks/useRealtimeRefetch';
import type { SagaListItem, SagaStats, PagedList } from '@/types';
import * as api from '@/api';

export default function SagasListPage() {
  const [data, setData] = useState<PagedList<SagaListItem> | null>(null);
  const [types, setTypes] = useState<string[]>([]);
  const [stats, setStats] = useState<SagaStats | null>(null);
  const [typeFilter, setTypeFilter] = useState<string>('');
  const [keyFilter, setKeyFilter] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [unavailable, setUnavailable] = useState(false);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();
  const refreshKey = useRefreshKey();

  const fetchAll = useCallback(async () => {
    try {
      const [list, typeList, statsData] = await Promise.all([
        api.listSagas(page, pageSize, typeFilter || undefined, keyFilter || undefined),
        api.getSagaTypes(),
        api.getSagaStats(),
      ]);
      setData(list);
      setTypes(typeList);
      setStats(statsData);
      setError(null);
      setUnavailable(false);
    } catch (e) {
      if (axios.isAxiosError(e) && e.response?.status === 404) {
        setUnavailable(true);
        setError(null);
        return;
      }
      setError('Unable to load sagas');
    }
  }, [page, pageSize, typeFilter, keyFilter]);

  useEffect(() => {
    fetchAll();
  }, [refreshKey, fetchAll]);

  // Live updates: saga lifecycle is driven by message arrivals (proxy commits inside
  // SaveChanges, which emits MessageEnqueued for routed children and JobFinalized when
  // those jobs settle). Either event indicates a saga may have changed.
  useRealtimeRefetch(['JobFinalized', 'MessageEnqueued'], fetchAll);

  if (unavailable) {
    return (
      <div>
        <h1 className="text-2xl font-bold mb-4">Sagas</h1>
        <Card>
          <CardContent className="py-8 text-center text-muted-foreground">
            Sagas addon is not registered. Call <code className="font-mono text-xs">opt.AddSagas()</code> in your Warp configuration to enable.
          </CardContent>
        </Card>
      </div>
    );
  }

  if (error) return <ErrorState message={error} />;
  if (!data) return <LoadingState />;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Sagas</h1>

      {stats && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-4">
          <Card>
            <CardContent className="py-4">
              <div className="text-sm text-muted-foreground">Live sagas</div>
              <div className="text-2xl font-bold">{stats.liveSagas}</div>
            </CardContent>
          </Card>
          <Card>
            <CardContent className="py-4">
              <div className="text-sm text-muted-foreground">Started today</div>
              <div className="text-2xl font-bold">{stats.startedToday}</div>
            </CardContent>
          </Card>
          <Card>
            <CardContent className="py-4">
              <div className="text-sm text-muted-foreground">Types in use</div>
              <div className="text-2xl font-bold">{types.length}</div>
            </CardContent>
          </Card>
        </div>
      )}

      <div className="flex gap-2 mb-4">
        <select
          className="border rounded-md px-2 py-1 text-sm bg-background"
          value={typeFilter}
          onChange={(e) => { setTypeFilter(e.target.value); setPage(0); }}
        >
          <option value="">All types</option>
          {types.map(t => <option key={t} value={t}>{shortName(t)}</option>)}
        </select>
        <input
          type="text"
          className="border rounded-md px-2 py-1 text-sm bg-background flex-1 max-w-xs"
          placeholder="Search correlation key…"
          value={keyFilter}
          onChange={(e) => { setKeyFilter(e.target.value); setPage(0); }}
        />
      </div>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Type</TableHead>
              <TableHead>Correlation key</TableHead>
              <TableHead>Updated</TableHead>
              <TableHead>Created</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={4} className="text-center text-muted-foreground py-8">
                  No sagas found
                </TableCell>
              </TableRow>
            ) : (
              data.items.map((s) => (
                <TableRow key={s.id}>
                  <TableCell className="font-medium">{shortName(s.type)}</TableCell>
                  <TableCell className="font-mono text-xs">
                    <Link to={`/sagas/${s.id}`} className="text-primary hover:underline">
                      {s.correlationKey}
                    </Link>
                  </TableCell>
                  <TableCell className="text-sm"><RelativeTime date={s.updatedAt} /></TableCell>
                  <TableCell className="text-sm text-muted-foreground"><RelativeTime date={s.createdAt} /></TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      <Pagination
        page={page}
        pageSize={pageSize}
        pageCount={Math.ceil(data.totalCount / pageSize)}
        onPageChange={setPage}
        onPageSizeChange={(size) => { setPageSize(size); setPage(0); }}
      />
    </div>
  );
}

function shortName(assemblyQualifiedName: string): string {
  const typeName = assemblyQualifiedName.split(',')[0];
  return typeName.split('.').pop() ?? typeName;
}
