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
import { ConcurrencyLimitFormDialog } from '@/components/forms/ConcurrencyLimitFormDialog';
import { ConfirmDialog } from '@/components/forms/ConfirmDialog';
import type { ConcurrencyLimitFormValues } from '@/lib/schemas/concurrencyLimit';
import type { ConcurrencyLimitInfo } from '@/types';
import {
  useConcurrencyLimits,
  useUpsertConcurrencyLimit,
  useDeleteConcurrencyLimit,
} from '@/api/hooks/useConcurrencyLimits';

type EditState =
  | { mode: 'create' }
  | { mode: 'edit'; initial: ConcurrencyLimitFormValues }
  | null;

export default function ConcurrencyLimitsPage() {
  const [editState, setEditState] = useState<EditState>(null);
  const [confirmDelete, setConfirmDelete] = useState<{ name: string } | null>(null);

  const query = useConcurrencyLimits();
  const upsert = useUpsertConcurrencyLimit();
  const remove = useDeleteConcurrencyLimit();

  const limits = useMemo(() => query.data ?? [], [query.data]);
  const existingNames = useMemo(() => new Set(limits.map((x) => x.name)), [limits]);

  const columns = useMemo<ColumnDef<ConcurrencyLimitInfo>[]>(() => [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => <span className="font-mono">{row.original.name}</span>,
    },
    {
      accessorKey: 'limit',
      header: 'Limit',
      cell: ({ row }) => <span className="font-mono">{row.original.limit}</span>,
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
                initial: { name: row.original.name, limit: row.original.limit },
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
        <h1 className="text-2xl font-bold mb-2">Concurrency Limits</h1>
        <Card>
          <CardContent className="py-8 text-center text-muted-foreground">
            Concurrency limits addon not registered. Add <code className="font-mono">opt.AddConcurrency()</code> to enable.
          </CardContent>
        </Card>
      </div>
    );
  }

  const handleSubmit = async (values: ConcurrencyLimitFormValues) => {
    await upsert.mutateAsync({ name: values.name, limit: values.limit });
  };

  const handleDelete = async () => {
    if (!confirmDelete) return;
    await remove.mutateAsync(confirmDelete.name);
    setConfirmDelete(null);
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <h1 className="text-2xl font-bold">Concurrency Limits</h1>
        <Button onClick={() => setEditState({ mode: 'create' })}>
          <Plus className="h-4 w-4" />
          Add limit
        </Button>
      </div>
      <p className="text-sm text-muted-foreground mb-4">
        Runtime overrides for <code>[Mutex]</code> and <code>[Semaphore]</code> keys. Admin row beats the attribute limit; takes effect on next pickup.
      </p>

      {!query.data ? (
        <TableSkeleton rows={6} headers={['Name', 'Limit', 'Updated', '']} />
      ) : (
        <DataTable
          columns={columns}
          data={limits}
          initialSorting={[{ id: 'name', desc: false }]}
          emptyMessage="No concurrency limits defined."
        />
      )}

      <ConcurrencyLimitFormDialog
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
        title="Delete concurrency limit?"
        description={
          confirmDelete ? (
            <>
              Remove the override for <code className="font-mono">{confirmDelete.name}</code>? Jobs will fall back to the attribute limit.
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
