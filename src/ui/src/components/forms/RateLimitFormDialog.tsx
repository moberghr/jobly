import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog';
import {
  Form,
  FormField,
  FormItem,
  FormLabel,
  FormControl,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { rateLimitSchema, type RateLimitFormValues } from '@/lib/schemas/rateLimit';

type Mode = 'create' | 'edit';

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  mode: Mode;
  initial?: RateLimitFormValues;
  existingNames?: Set<string>;
  onSubmit: (values: RateLimitFormValues) => Promise<void>;
};

const defaultValues: RateLimitFormValues = {
  name: '',
  count: 100,
  windowSeconds: 60,
};

export function RateLimitFormDialog({
  open,
  onOpenChange,
  mode,
  initial,
  existingNames,
  onSubmit,
}: Props) {
  const form = useForm<RateLimitFormValues>({
    resolver: zodResolver(rateLimitSchema),
    defaultValues: initial ?? defaultValues,
  });

  useEffect(() => {
    if (open) {
      form.reset(initial ?? defaultValues);
    }
  }, [open, initial, form]);

  const handleSubmit = form.handleSubmit(async (values) => {
    const trimmed = values.name.trim();
    if (mode === 'create' && existingNames?.has(trimmed)) {
      form.setError('name', { message: 'A rate limit with that name already exists' });

      return;
    }
    try {
      await onSubmit({ ...values, name: trimmed });
      toast.success(mode === 'create' ? 'Rate limit added' : 'Rate limit updated');
      onOpenChange(false);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to save';
      toast.error(message);
    }
  });

  const isSubmitting = form.formState.isSubmitting;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{mode === 'create' ? 'Add rate limit' : 'Edit rate limit'}</DialogTitle>
          <DialogDescription>
            Runtime override for a <code className="font-mono">[RateLimit]</code> key. Takes effect on next pickup.
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={handleSubmit} className="space-y-4">
            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="e.g. external-api"
                      className="font-mono"
                      disabled={mode === 'edit'}
                      autoFocus={mode === 'create'}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="count"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Count</FormLabel>
                  <FormControl>
                    <Input
                      type="number"
                      min={1}
                      value={field.value ?? ''}
                      onChange={(e) => field.onChange(e.target.value === '' ? NaN : Number(e.target.value))}
                      onBlur={field.onBlur}
                      name={field.name}
                      ref={field.ref}
                      autoFocus={mode === 'edit'}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="windowSeconds"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Window (seconds)</FormLabel>
                  <FormControl>
                    <Input
                      type="number"
                      min={1}
                      value={field.value ?? ''}
                      onChange={(e) => field.onChange(e.target.value === '' ? NaN : Number(e.target.value))}
                      onBlur={field.onBlur}
                      name={field.name}
                      ref={field.ref}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <DialogFooter>
              <Button
                type="button"
                variant="ghost"
                onClick={() => onOpenChange(false)}
                disabled={isSubmitting}
              >
                Cancel
              </Button>
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? 'Saving...' : 'Save'}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
