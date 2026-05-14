import { useState, useEffect, useCallback } from 'react';
import axios from 'axios';
import { toast } from 'sonner';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { LoadingState, ErrorState } from '@/components/PageState';
import { RelativeTime } from '@/components/RelativeTime';
import { useRefreshKey } from '@/hooks/useRefreshKey';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import type { RateLimitInfo } from '@/types';
import * as api from '@/api';
import { RateLimitFormDialog } from '@/components/forms/RateLimitFormDialog';
import { ConfirmDialog } from '@/components/forms/ConfirmDialog';
import type { RateLimitFormValues } from '@/lib/schemas/rateLimit';

type EditState =
  | { mode: 'create' }
  | { mode: 'edit'; initial: RateLimitFormValues }
  | null;

export default function RateLimitsPage() {
  const [limits, setLimits] = useState<RateLimitInfo[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [unavailable, setUnavailable] = useState(false);
  const [editState, setEditState] = useState<EditState>(null);
  const [confirmDelete, setConfirmDelete] = useState<{ name: string } | null>(null);
  const refreshKey = useRefreshKey();

  const fetchAll = useCallback(async () => {
    try {
      const result = await api.listRateLimits();
      setLimits(result);
      setError(null);
      setUnavailable(false);
    } catch (e) {
      if (axios.isAxiosError(e) && e.response?.status === 404) {
        setUnavailable(true);
        setLimits([]);
        setError(null);

        return;
      }
      setError('Unable to load rate limits');
    }
  }, []);

  useEffect(() => {
    fetchAll();
    const id = setInterval(fetchAll, 5000);
    return () => clearInterval(id);
  }, [refreshKey, fetchAll]);

  const handleSubmit = async (values: RateLimitFormValues) => {
    await api.upsertRateLimit(values.name, values.count, values.windowSeconds);
    await fetchAll();
  };

  const handleDelete = async () => {
    if (!confirmDelete) {
      return;
    }
    try {
      await api.deleteRateLimit(confirmDelete.name);
      toast.success('Rate limit deleted');
      setConfirmDelete(null);
      await fetchAll();
    } catch {
      toast.error('Failed to delete rate limit');
    }
  };

  if (error) return <ErrorState message={error} />;
  if (!limits) return <LoadingState />;

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

  const sorted = [...limits].sort((a, b) => a.name.localeCompare(b.name));
  const existingNames = new Set(limits.map((x) => x.name));

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

      <Card>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead className="w-32">Count</TableHead>
                <TableHead className="w-36">Window (s)</TableHead>
                <TableHead>Updated</TableHead>
                <TableHead className="text-right w-32">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {sorted.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5} className="text-center text-muted-foreground py-8">
                    No rate limits defined.
                  </TableCell>
                </TableRow>
              ) : (
                sorted.map((limit) => (
                  <TableRow key={limit.name}>
                    <TableCell className="font-mono">{limit.name}</TableCell>
                    <TableCell className="font-mono">{limit.count}</TableCell>
                    <TableCell className="font-mono">{limit.windowSeconds}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      <RelativeTime date={limit.updatedAt} />
                    </TableCell>
                    <TableCell className="text-right">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() =>
                          setEditState({
                            mode: 'edit',
                            initial: {
                              name: limit.name,
                              count: limit.count,
                              windowSeconds: limit.windowSeconds,
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
                        onClick={() => setConfirmDelete({ name: limit.name })}
                      >
                        <Trash2 className="h-4 w-4" />
                        Delete
                      </Button>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

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
