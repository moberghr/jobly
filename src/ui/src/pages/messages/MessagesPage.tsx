import { useState, useEffect, useCallback } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortType, shortId } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import { useRefreshKey } from '@/hooks/useRefreshKey';
import type { MessageModel, PagedList } from '@/types';
import * as api from '@/api';

export default function MessagesPage() {
  const { state } = useParams<{ state?: string }>();
  const [data, setData] = useState<PagedList<MessageModel> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();
  const refreshKey = useRefreshKey();

  useEffect(() => { setPage(0); }, [state]);

  const fetchData = useCallback(async () => {
    try {
      const result = await api.getMessages(page, pageSize, state);
      setData(result);
      setError(null);
    } catch {
      setError('Unable to load messages');
    }
  }, [page, pageSize, state]);

  useEffect(() => { fetchData(); }, [fetchData, refreshKey]);

  if (error) return <ErrorState message={error} />;
  if (!data) return <LoadingState />;

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold">{state ? `${state.charAt(0).toUpperCase() + state.slice(1)} Messages` : 'Messages'}</h1>
        <span className="text-sm text-muted-foreground">{data.totalCount} total</span>
      </div>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[100px]">ID</TableHead>
              <TableHead>Type</TableHead>
              <TableHead>Queue</TableHead>
              <TableHead>State</TableHead>
              <TableHead>Jobs</TableHead>
              <TableHead>Created</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={6} className="text-center text-muted-foreground py-8">
                  No messages found
                </TableCell>
              </TableRow>
            ) : (
              data.items.map((msg) => (
                <TableRow key={msg.id}>
                  <TableCell className="font-mono text-xs">
                    <Link to={`/messages/detail/${msg.id}`} className="text-primary hover:underline">
                      {shortId(msg.id)}
                    </Link>
                  </TableCell>
                  <TableCell>{shortType(msg.type)}</TableCell>
                  <TableCell>{msg.queue}</TableCell>
                  <TableCell><StateBadge state={msg.currentState} /></TableCell>
                  <TableCell>{msg.jobCount}</TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    <RelativeTime date={msg.createTime} />
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={(size) => { setPageSize(size); setPage(0); }} />
    </div>
  );
}
