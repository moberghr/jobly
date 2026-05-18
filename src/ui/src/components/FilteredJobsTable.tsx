import { useEffect, useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import {
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
  type ColumnFiltersState,
  type RowSelectionState,
} from '@tanstack/react-table';
import { toast } from 'sonner';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Checkbox } from '@/components/ui/checkbox';
import { StateBadge } from '@/components/StateBadge';
import { Pagination } from '@/components/Pagination';
import { shortType, shortId } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { State } from '@/types';
import type { JobModel, PagedList } from '@/types';
import { JobsTableSkeleton } from '@/components/skeletons/JobsTableSkeleton';
import { useRequeueJob, useDeleteJob } from '@/api/hooks/useJobs';
import { queryScopes } from '@/lib/queryClient';
import { ChevronDown, ChevronUp, ChevronsUpDown } from 'lucide-react';

const stateItems = [
  { key: 'awaiting', label: 'Awaiting', color: 'bg-orange-100 text-orange-700 dark:bg-orange-900 dark:text-orange-300' },
  { key: 'scheduled', label: 'Scheduled', color: 'bg-amber-100 text-amber-700 dark:bg-amber-900 dark:text-amber-300' },
  { key: 'enqueued', label: 'Enqueued', color: 'bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300' },
  { key: 'processing', label: 'Processing', color: 'bg-purple-100 text-purple-700 dark:bg-purple-900 dark:text-purple-300' },
  { key: 'completed', label: 'Completed', color: 'bg-green-100 text-green-700 dark:bg-green-900 dark:text-green-300' },
  { key: 'failed', label: 'Failed', color: 'bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-300' },
  { key: 'deleted', label: 'Deleted', color: 'bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300' },
];

interface FilteredJobsTableProps {
  title: string;
  parentId: string;
  parentKind: 'batch' | 'message';
  fetchJobs: (page: number, pageSize: number, state?: string) => Promise<PagedList<JobModel>>;
  fetchCounts: () => Promise<Record<string, number>>;
  onCountsUpdate?: (counts: Record<string, number>) => void;
}

const DEFAULT_PAGE_SIZE = 20;

export function FilteredJobsTable({ title, parentId, parentKind, fetchJobs, fetchCounts, onCountsUpdate }: FilteredJobsTableProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const [selectedState, setSelectedState] = useState<string | null>(null);

  const page = Number(searchParams.get('page') ?? '0') || 0;
  const pageSize = Number(searchParams.get('pageSize') ?? String(DEFAULT_PAGE_SIZE)) || DEFAULT_PAGE_SIZE;

  const setPage = (next: number) => {
    const params = new URLSearchParams(searchParams);
    if (next === 0) {
      params.delete('page');
    } else {
      params.set('page', String(next));
    }
    setSearchParams(params, { replace: true });
  };

  const [sorting, setSorting] = useState<SortingState>([{ id: 'createTime', desc: true }]);
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
  const [rowSelection, setRowSelection] = useState<RowSelectionState>({});

  const countsQuery = useQuery({
    queryKey: [parentKind, parentId, 'jobs', 'counts'],
    queryFn: fetchCounts,
  });

  const counts = countsQuery.data ?? {};
  const countsError = countsQuery.isError;

  useEffect(() => {
    if (countsQuery.data) {
      onCountsUpdate?.(countsQuery.data);
    }
  }, [countsQuery.data, onCountsUpdate]);

  useEffect(() => {
    if (!countsQuery.data || selectedState !== null) {
      return;
    }
    const best = stateItems.reduce(
      (max, item) => (countsQuery.data[item.key] ?? 0) > (countsQuery.data[max.key] ?? 0) ? item : max,
      stateItems[0],
    );
    setSelectedState(best.key);
  }, [countsQuery.data, selectedState]);

  const jobsQuery = useQuery({
    queryKey: [parentKind, parentId, 'jobs', selectedState, page, pageSize],
    queryFn: () => fetchJobs(page, pageSize, selectedState ?? undefined),
    enabled: selectedState !== null,
  });

  const data = jobsQuery.data ?? null;
  const requeueJob = useRequeueJob();
  const deleteJob = useDeleteJob();
  const qc = useQueryClient();

  // Reset selection on state/page change.
  useEffect(() => {
    setRowSelection({});
  }, [selectedState, page, pageSize]);

  const rows = data?.items ?? [];

  const columns = useMemo<ColumnDef<JobModel>[]>(() => [
    {
      id: 'select',
      header: ({ table }) => (
        <Checkbox
          checked={table.getIsAllRowsSelected()}
          indeterminate={table.getIsSomeRowsSelected()}
          onChange={table.getToggleAllRowsSelectedHandler()}
          aria-label="Select all"
        />
      ),
      cell: ({ row }) => (
        <Checkbox
          checked={row.getIsSelected()}
          onChange={row.getToggleSelectedHandler()}
          aria-label="Select row"
        />
      ),
      enableSorting: false,
      size: 40,
    },
    {
      accessorKey: 'createTime',
      header: 'Created',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          <RelativeTime date={row.original.createTime} />
        </span>
      ),
    },
    {
      accessorKey: 'type',
      header: 'Type',
      filterFn: (row, _id, filterValue: string) => {
        const v = String(row.original.type ?? '').toLowerCase();
        return v.includes(String(filterValue).toLowerCase());
      },
      cell: ({ row }) => (
        <div>
          <div className="text-sm">{shortType(row.original.type)}</div>
          {row.original.handlerType && (
            <div className="text-xs text-muted-foreground">{shortType(row.original.handlerType)}</div>
          )}
        </div>
      ),
    },
    {
      accessorKey: 'id',
      header: 'Id',
      enableSorting: false,
      cell: ({ row }) => (
        <Link to={`/detail/${row.original.id}`} className="text-primary hover:underline font-mono text-xs">
          {shortId(row.original.id)}
        </Link>
      ),
    },
    {
      accessorKey: 'currentState',
      header: 'State',
      cell: ({ row }) => (
        <StateBadge state={row.original.currentState} cancellationMode={row.original.cancellationMode} />
      ),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: ({ row }) => (
        <div className="text-right">
          {row.original.currentState === State.Failed && (
            <Button variant="outline" size="sm" className="h-7 text-xs" onClick={() => requeueJob.mutate(row.original.id)}>
              Requeue
            </Button>
          )}
        </div>
      ),
    },
  ], [requeueJob]);

  const table = useReactTable({
    data: rows,
    columns,
    state: { sorting, columnFilters, rowSelection },
    onSortingChange: setSorting,
    onColumnFiltersChange: setColumnFilters,
    onRowSelectionChange: setRowSelection,
    getRowId: (row) => row.id,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    enableRowSelection: true,
  });

  const selectedIds = Object.keys(rowSelection).filter((id) => rowSelection[id]);

  const handleBulkRequeue = async () => {
    let ok = 0;
    let fail = 0;
    for (const id of selectedIds) {
      try {
        await requeueJob.mutateAsync(id);
        ok++;
      } catch {
        fail++;
      }
    }
    toast.success(`Requeued ${ok} job${ok === 1 ? '' : 's'}${fail ? ` (${fail} failed)` : ''}`);
    setRowSelection({});
    qc.invalidateQueries({ queryKey: queryScopes.jobs });
  };

  const handleBulkDelete = async () => {
    let ok = 0;
    let fail = 0;
    for (const id of selectedIds) {
      try {
        await deleteJob.mutateAsync(id);
        ok++;
      } catch {
        fail++;
      }
    }
    toast.success(`Deleted ${ok} job${ok === 1 ? '' : 's'}${fail ? ` (${fail} failed)` : ''}`);
    setRowSelection({});
    qc.invalidateQueries({ queryKey: queryScopes.jobs });
  };

  const typeFilter = (table.getColumn('type')?.getFilterValue() as string | undefined) ?? '';

  return (
    <div>
      <div className="flex items-center gap-2 mb-3">
        <h2 className="text-lg font-semibold">{title}</h2>
        {Object.keys(counts).length > 0 && (
          <span className="text-sm text-muted-foreground">({Object.values(counts).reduce((a, b) => a + b, 0)})</span>
        )}
      </div>
      {countsError && (
        <div className="text-xs text-destructive bg-destructive/10 border border-destructive/20 rounded-md px-3 py-1.5 mb-2">
          Unable to refresh counts — showing last known data
        </div>
      )}
      <div className="flex flex-col md:flex-row gap-4">
        {/* Vertical state sidebar (per-table filter, not the global EntityStateSidebar) */}
        <nav className="md:w-44 shrink-0 space-y-1">
          {stateItems.map((item) => {
            const isActive = selectedState === item.key;
            return (
              <button
                key={item.label}
                onClick={() => {
                  if (isActive) {
                    jobsQuery.refetch();
                  } else {
                    setSelectedState(item.key);
                    setPage(0);
                  }
                }}
                className={`w-full flex items-center justify-between px-3 py-2 rounded-md text-sm transition-colors text-left ${
                  isActive
                    ? 'bg-accent text-accent-foreground font-medium'
                    : 'text-muted-foreground hover:bg-accent/50'
                }`}
              >
                <span>{item.label}</span>
                <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                  (counts[item.key] ?? 0) > 0 ? item.color : 'text-muted-foreground/50'
                }`}>
                  {counts[item.key] ?? 0}
                </span>
              </button>
            );
          })}
        </nav>

        {/* Jobs table */}
        <div className="flex-1 min-w-0">
          {data && data.items.length > 0 ? (
            <>
              {/* Toolbar: type filter + bulk action bar */}
              <div className="flex flex-wrap items-center gap-2 mb-3">
                <Input
                  placeholder="Filter type..."
                  value={typeFilter}
                  onChange={(e) => table.getColumn('type')?.setFilterValue(e.target.value)}
                  className="h-8 w-56"
                />
                {selectedIds.length > 0 && (
                  <BulkActionBar
                    selectedCount={selectedIds.length}
                    onRequeue={handleBulkRequeue}
                    onDelete={handleBulkDelete}
                    onClear={() => setRowSelection({})}
                  />
                )}
              </div>

              <div className="rounded-md border overflow-x-auto">
                <Table>
                  <TableHeader>
                    {table.getHeaderGroups().map((hg) => (
                      <TableRow key={hg.id}>
                        {hg.headers.map((header) => {
                          const canSort = header.column.getCanSort();
                          const sorted = header.column.getIsSorted();
                          return (
                            <TableHead key={header.id}>
                              {header.isPlaceholder ? null : canSort ? (
                                <button
                                  type="button"
                                  onClick={header.column.getToggleSortingHandler()}
                                  className="inline-flex items-center gap-1 hover:text-foreground transition-colors"
                                >
                                  {flexRender(header.column.columnDef.header, header.getContext())}
                                  {sorted === 'asc' ? <ChevronUp className="h-3 w-3" /> : sorted === 'desc' ? <ChevronDown className="h-3 w-3" /> : <ChevronsUpDown className="h-3 w-3 opacity-40" />}
                                </button>
                              ) : (
                                flexRender(header.column.columnDef.header, header.getContext())
                              )}
                            </TableHead>
                          );
                        })}
                      </TableRow>
                    ))}
                  </TableHeader>
                  <TableBody>
                    {table.getRowModel().rows.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={columns.length} className="text-center text-muted-foreground py-8">
                          No jobs match the current filter.
                        </TableCell>
                      </TableRow>
                    ) : (
                      table.getRowModel().rows.map((row) => (
                        <TableRow key={row.id} data-state={row.getIsSelected() ? 'selected' : undefined}>
                          {row.getVisibleCells().map((cell) => (
                            <TableCell key={cell.id}>
                              {flexRender(cell.column.columnDef.cell, cell.getContext())}
                            </TableCell>
                          ))}
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
              </div>
              {data.pageCount > 1 && (
                <Pagination page={page} pageCount={data.pageCount} onPageChange={setPage} />
              )}
            </>
          ) : data ? (
            <div className="text-sm text-muted-foreground py-4 text-center">
              No jobs found
            </div>
          ) : (
            <JobsTableSkeleton rows={8} />
          )}
        </div>
      </div>
    </div>
  );
}

interface BulkActionBarProps {
  selectedCount: number;
  onRequeue: () => void;
  onDelete: () => void;
  onClear: () => void;
}

function BulkActionBar({ selectedCount, onRequeue, onDelete, onClear }: BulkActionBarProps) {
  return (
    <div className="flex items-center gap-2 px-3 py-1.5 bg-muted rounded-md">
      <span className="text-sm font-medium">{selectedCount} selected</span>
      <Button variant="outline" size="sm" onClick={onRequeue}>Requeue selected</Button>
      <Button variant="outline" size="sm" className="text-destructive" onClick={onDelete}>Delete selected</Button>
      <Button variant="ghost" size="sm" onClick={onClear}>Clear</Button>
    </div>
  );
}
