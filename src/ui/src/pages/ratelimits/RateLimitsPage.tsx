import { useMemo, useState } from 'react';
import axios from 'axios';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { ErrorState } from '@/components/PageState';
import { RelativeTime } from '@/components/RelativeTime';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import { TableSkeleton } from '@/components/skeletons/TableSkeleton';
import { RateLimitFormDialog } from '@/components/forms/RateLimitFormDialog';
import { ConfirmDialog } from '@/components/forms/ConfirmDialog';
import type { RateLimitFormValues } from '@/lib/schemas/rateLimit';
import type { RateLimitInfo } from '@/types';
import {
  useRateLimits,
  useUpsertRateLimit,
  useDeleteRateLimit,
} from '@/api/hooks/useRateLimits';

type EditState =
  | { mode: 'create' }
  | { mode: 'edit'; initial: RateLimitFormValues }
  | null;

export default function RateLimitsPage() {
  const [editState, setEditState] = useState<EditState>(null);
  const [confirmDelete, setConfirmDelete] = useState<{ name: string } | null>(null);

  const query = useRateLimits();
  const upsert = useUpsertRateLimit();
  const remove = useDeleteRateLimit();

  const limits = useMemo(() => query.data ?? [], [query.data]);
  const existingNames = useMemo(() => new Set(limits.map((x) => x.name)), [limits]);

  const columns = useMemo<ColumnDef<RateLimitInfo>[]>(() => [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => <span className="font-mono">{row.original.name}</span>,
    },
    {
      accessorKey: 'count',
      header: 'Count',
      cell: ({ row }) => <span className="font-mono">{row.original.count}</span>,
    },
    {
      accessorKey: 'windowSeconds',
      header: 'Window (s)',
      cell: ({ row }) => <span className="font-mono">{row.original.windowSeconds}</span>,
    },
    {
      accessorKey: 'updatedAt',
      header: 'Updated',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          <RelativeTime date={row.original.updatedAt} />
        </span>
      ),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: ({ row }) => (
        <div className="text-right">
          <Button
            variant="ghost"
            size="sm"
            onClick={() =>
              setEditState({
                mode: 'edit',
                initial: {
                  name: row.original.name,
                  count: row.original.count,
                  windowSeconds: row.original.windowSeconds,
                },
              })
            }
          >
            <Pencil className="h-4 w-4" />
            Edit
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="text-destructive"
            onClick={() => setConfirmDelete({ name: row.original.name })}
          >
            <Trash2 className="h-4 w-4" />
            Delete
          </Button>
        </div>
      ),
    },
  ], []);

  const unavailable =
    query.error !== null &&
    query.error !== undefined &&
    axios.isAxiosError(query.error) &&
    query.error.response?.status === 404;

  if (query.error && !unavailable) return <ErrorState message={(query.error as Error).message} />;

  if (unavailable) {
    return (
      <div>
        <h1 className="text-2xl font-bold mb-2">Rate Limits</h1>
        <Card>
          <CardContent className="py-8 text-center text-muted-foreground">
            Rate limits addon not registered. Add <code className="font-mono">opt.AddRateLimit()</code> to enable.
          </CardContent>
        </Card>
      </div>
    );
  }

  const handleSubmit = async (values: RateLimitFormValues) => {
    await upsert.mutateAsync({
      name: values.name,
      count: values.count,
      windowSeconds: values.windowSeconds,
    });
  };

  const handleDelete = async () => {
    if (!confirmDelete) return;
    await remove.mutateAsync(confirmDelete.name);
    setConfirmDelete(null);
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <h1 className="text-2xl font-bold">Rate Limits</h1>
        <Button onClick={() => setEditState({ mode: 'create' })}>
          <Plus className="h-4 w-4" />
          Add rate limit
        </Button>
      </div>
      <p className="text-sm text-muted-foreground mb-4">
        Runtime overrides for <code>[RateLimit]</code> keys. Admin row beats the attribute count and window; takes effect on next pickup.
      </p>

      {!query.data ? (
        <TableSkeleton rows={6} headers={['Name', 'Count', 'Window (s)', 'Updated', '']} />
      ) : (
        <DataTable
          columns={columns}
          data={limits}
          initialSorting={[{ id: 'name', desc: false }]}
          emptyMessage="No rate limits defined."
        />
      )}

      <RateLimitFormDialog
        open={editState !== null}
        onOpenChange={(open) => {
          if (!open) {
            setEditState(null);
          }
        }}
        mode={editState?.mode ?? 'create'}
        initial={editState?.mode === 'edit' ? editState.initial : undefined}
        existingNames={existingNames}
        onSubmit={handleSubmit}
      />

      <ConfirmDialog
        open={confirmDelete !== null}
        onOpenChange={(open) => {
          if (!open) {
            setConfirmDelete(null);
          }
        }}
        title="Delete rate limit?"
        description={
          confirmDelete ? (
            <>
              Remove the override for <code className="font-mono">{confirmDelete.name}</code>? Jobs will fall back to the attribute count and window.
            </>
          ) : null
        }
        confirmLabel="Delete"
        destructive
        onConfirm={handleDelete}
      />
    </div>
  );
}
