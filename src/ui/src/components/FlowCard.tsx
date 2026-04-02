import { Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { StateBadge } from '@/components/StateBadge';
import { shortId, shortType } from '@/utils/format';
import type { ContinuationInfo } from '@/types';

interface FlowCardProps {
  parentJobId?: string | null;
  parentJobKind?: number | null;
  traceId?: string | null;
  continuations: ContinuationInfo[];
}

function linkForKind(id: string, kind: number | null | undefined) {
  if (kind === 3) return `/batches/detail/${id}`;
  if (kind === 2) return `/messages/detail/${id}`;
  return `/jobs/detail/${id}`;
}

function kindLabel(kind: number | null | undefined) {
  if (kind === 3) return 'Batch';
  if (kind === 2) return 'Message';
  return 'Job';
}

export function FlowCard({ parentJobId, parentJobKind, traceId, continuations }: FlowCardProps) {
  const hasContent = parentJobId || traceId || continuations.length > 0;
  if (!hasContent) return null;

  return (
    <Card>
      <CardHeader className="pb-2"><CardTitle className="text-sm">Flow</CardTitle></CardHeader>
      <CardContent className="space-y-3 text-sm">
        {parentJobId && (
          <div>
            <span className="text-muted-foreground">Parent:</span>{' '}
            <Link to={linkForKind(parentJobId, parentJobKind)} className="text-primary hover:underline font-mono text-xs">
              {shortId(parentJobId)}
            </Link>
            <span className="text-xs text-muted-foreground ml-2">({kindLabel(parentJobKind)})</span>
          </div>
        )}
        {traceId && (
          <div>
            <span className="text-muted-foreground">Trace:</span>{' '}
            <span className="font-mono text-xs">{shortId(traceId)}</span>
          </div>
        )}
        {continuations.length > 0 && (
          <div>
            <div className="text-muted-foreground mb-2">Continuations ({continuations.length})</div>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>ID</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Handler</TableHead>
                  <TableHead>Kind</TableHead>
                  <TableHead>State</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {continuations.map(c => (
                  <TableRow key={c.id}>
                    <TableCell className="font-mono text-xs">
                      <Link to={linkForKind(c.id, c.kind)} className="text-primary hover:underline">
                        {shortId(c.id)}
                      </Link>
                    </TableCell>
                    <TableCell>{shortType(c.type)}</TableCell>
                    <TableCell>{c.handlerType ? shortType(c.handlerType) : '—'}</TableCell>
                    <TableCell className="text-xs">{kindLabel(c.kind)}</TableCell>
                    <TableCell><StateBadge state={c.currentState} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
