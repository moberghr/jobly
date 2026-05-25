import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import type { ColumnDef } from '@tanstack/react-table';
import { Button } from '@/components/ui/button';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { DataTable } from '@/components/DataTable';
import { usePersistedPageSize } from '@/hooks/usePersistedPageSize';
import {
  useRecurringList,
  useEnableRecurringJob,
  useDisableRecurringJob,
  useTriggerRecurringJob,
  useDeleteRecurringJob,
} from '@/api/hooks/useRecurring';
import type { RecurringJobModel } from '@/types';

export default function RecurringPage() {
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = usePersistedPageSize();
  const { data, isLoading, isError } = useRecurringList(page, pageSize);

  const enable = useEnableRecurringJob();
  const disable = useDisableRecurringJob();
  const trigger = useTriggerRecurringJob();
  const remove = useDeleteRecurringJob();

  const columns = useMemo<ColumnDef<RecurringJobModel>[]>(
    () => [
      {
        accessorKey: 'name',
        header: 'Name',
        cell: ({ row }) => (
          <Link
            to={`/recurring/${row.original.id}`}
            className="font-medium text-primary hover:underline"
          >
            {row.original.name}
          </Link>
        ),
      },
      {
        accessorKey: 'cron',
        header: 'Cron',
        cell: ({ row }) => <span className="font-mono text-xs">{row.original.cron}</span>,
      },
      {
        accessorKey: 'type',
        header: 'Type',
        cell: ({ row }) => row.original.type.split(',')[0].split('.').pop(),
      },
      {
        id: 'status',
        header: 'Status',
        cell: ({ row }) =>
          row.original.disabledAt ? (
            <span className="inline-flex items-center rounded-full bg-orange-100 px-2 py-0.5 text-xs font-medium text-orange-800 dark:bg-orange-900/30 dark:text-orange-400">
              Disabled
            </span>
          ) : (
            <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800 dark:bg-green-900/30 dark:text-green-400">
              Enabled
            </span>
          ),
      },
      {
        accessorKey: 'nextExecution',
        header: 'Next Execution',
        cell: ({ row }) =>
          row.original.nextExecution ? (
            <span className="text-sm">
              <RelativeTime date={row.original.nextExecution} />
            </span>
          ) : (
            <span className="text-sm">N/A</span>
          ),
      },
      {
        accessorKey: 'lastExecution',
        header: 'Last Execution',
        cell: ({ row }) =>
          row.original.lastExecution ? (
            <span className="text-sm text-muted-foreground">
              <RelativeTime date={row.original.lastExecution} />
            </span>
          ) : (
            <span className="text-sm text-muted-foreground">Never</span>
          ),
      },
      {
        id: 'actions',
        header: 'Actions',
        cell: ({ row }) => (
          <>
            {row.original.disabledAt ? (
              <Button variant="ghost" size="sm" onClick={() => enable.mutate(row.original.id)}>
                Enable
              </Button>
            ) : (
              <Button variant="ghost" size="sm" onClick={() => disable.mutate(row.original.id)}>
                Disable
              </Button>
            )}
            <Button variant="ghost" size="sm" onClick={() => trigger.mutate(row.original.id)}>
              Trigger
            </Button>
            <Button
              variant="ghost"
              size="sm"
              className="text-destructive"
              onClick={() => remove.mutate(row.original.id)}
            >
              Remove
            </Button>
          </>
        ),
        meta: { headerClassName: 'text-right', cellClassName: 'text-right' },
      },
    ],
    [enable, disable, trigger, remove],
  );

  if (isError) return <ErrorState message="Unable to load recurring jobs" />;
  if (isLoading || !data) return <LoadingState />;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Recurring Jobs</h1>

      <DataTable
        columns={columns}
        data={data.items}
        emptyMessage="No recurring jobs found"
        getRowId={(row) => String(row.id)}
        pagination={{
          page,
          pageSize,
          pageCount: data.pageCount,
          onPageChange: setPage,
          onPageSizeChange: (size) => {
            setPageSize(size);
            setPage(0);
          },
        }}
      />
    </div>
  );
}
