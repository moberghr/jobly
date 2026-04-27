import { Card, CardContent } from '@/components/ui/card';
import { AlertTriangle, Loader } from 'lucide-react';

export function LoadingState() {
  return (
    <div className="flex items-center gap-2 text-muted-foreground py-8">
      <Loader className="h-4 w-4 animate-spin" />
      <span>Loading...</span>
    </div>
  );
}

export function ErrorState({ message }: { message?: string }) {
  return (
    <Card>
      <CardContent className="py-12 text-center">
        <AlertTriangle className="h-10 w-10 text-destructive mx-auto mb-3" />
        <p className="text-lg font-medium">{message ?? 'Something went wrong'}</p>
        <p className="text-sm text-muted-foreground mt-1">
          Make sure the Warp backend is running and accessible.
        </p>
      </CardContent>
    </Card>
  );
}

export function EmptyState({ message }: { message: string }) {
  return (
    <div className="text-center text-muted-foreground py-12">
      {message}
    </div>
  );
}
