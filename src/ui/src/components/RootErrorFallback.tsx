import { AlertTriangle } from 'lucide-react';
import type { FallbackProps } from 'react-error-boundary';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';

export function RootErrorFallback({ error, resetErrorBoundary }: FallbackProps) {
  const message = error instanceof Error ? error.message : String(error);
  return (
    <div className="min-h-screen flex items-center justify-center p-6 bg-background">
      <Card className="max-w-lg w-full">
        <CardContent className="py-10 text-center space-y-4">
          <AlertTriangle className="h-12 w-12 text-destructive mx-auto" />
          <div className="space-y-1">
            <p className="text-lg font-semibold">Something went wrong</p>
            <p className="text-sm text-muted-foreground">
              The dashboard hit an unexpected error. Reloading usually fixes it.
            </p>
          </div>
          <pre className="text-xs text-left bg-muted rounded p-3 overflow-auto max-h-40">
            {message}
          </pre>
          <div className="flex gap-2 justify-center">
            <Button variant="outline" onClick={resetErrorBoundary}>
              Try again
            </Button>
            <Button onClick={() => window.location.reload()}>Reload page</Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
