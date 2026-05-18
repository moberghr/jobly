import { Skeleton } from '@/components/ui/skeleton';
import { Panel } from '@/components/v2/Panel';

export function JobsTableSkeleton({ rows = 8 }: { rows?: number }) {
  return (
    <Panel className="overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full border-collapse">
          <thead>
            <tr className="bg-panel-2 border-b border-border">
              <th className="warp-eyebrow text-left px-3.5 py-2.5 text-text-mute font-semibold w-[80px]">ID</th>
              <th className="warp-eyebrow text-left px-3.5 py-2.5 text-text-mute font-semibold">Type</th>
              <th className="warp-eyebrow text-left px-3.5 py-2.5 text-text-mute font-semibold">Handler</th>
              <th className="warp-eyebrow text-right px-3.5 py-2.5 text-text-mute font-semibold w-[100px]">State</th>
              <th className="warp-eyebrow text-right px-3.5 py-2.5 text-text-mute font-semibold w-[120px]">Created</th>
              <th className="w-[80px]" />
            </tr>
          </thead>
          <tbody>
            {Array.from({ length: rows }).map((_, i) => (
              <tr key={i} className="border-b border-border last:border-b-0">
                <td className="px-3.5 py-2"><Skeleton className="h-4 w-12" /></td>
                <td className="px-3.5 py-2"><Skeleton className="h-4 w-40" /></td>
                <td className="px-3.5 py-2"><Skeleton className="h-4 w-32" /></td>
                <td className="px-3.5 py-2 text-right"><Skeleton className="h-5 w-16 ml-auto rounded-full" /></td>
                <td className="px-3.5 py-2 text-right"><Skeleton className="h-4 w-16 ml-auto" /></td>
                <td />
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
