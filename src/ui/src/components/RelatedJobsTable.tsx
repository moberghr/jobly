import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortType, shortId } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import type { JobModel, PagedList } from '@/types';

interface RelatedJobsTableProps {
  title: string;
  count: number;
  fetchJobs: (page: number, pageSize: number) => Promise<PagedList<JobModel>>;
}

export function RelatedJobsTable({ title, count, fetchJobs }: RelatedJobsTableProps) {
  const [data, setData] = useState<PagedList<JobModel> | null>(null);
  const [page, setPage] = useState(0);
  const pageSize = 10;

  const load = useCallback(async () => {
    try {
      const result = await fetchJobs(page, pageSize);
      setData(result);
    } catch {
      // Non-critical
    }
  }, [fetchJobs, page]);

  useEffect(() => { load(); }, [load]);

  if (count === 0) return null;

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm">{title} ({count})</CardTitle>
      </CardHeader>
      <CardContent>
        {data && data.items.length > 0 ? (
          <>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>ID</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>State</TableHead>
                  <TableHead>Created</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((job) => (
                  <TableRow key={job.id}>
                    <TableCell className="font-mono text-xs">
                      <Link to={`/jobs/detail/${job.id}`} className="text-primary hover:underline">{shortId(job.id)}</Link>
                    </TableCell>
                    <TableCell>{shortType(job.type)}</TableCell>
                    <TableCell><StateBadge state={job.currentState} /></TableCell>
                    <TableCell className="text-sm"><RelativeTime date={job.createTime} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            {data.pageCount > 1 && (
              <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} />
            )}
          </>
        ) : (
          <p className="text-muted-foreground text-sm py-2 text-center">Loading...</p>
        )}
      </CardContent>
    </Card>
  );
}
