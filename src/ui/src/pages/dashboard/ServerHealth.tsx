import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Panel } from '@/components/v2/Panel';
import { PulseDot } from '@/components/v2/PulseDot';
import { useServers } from '@/api/hooks/useServers';
import type { ServerModel, WorkerModel } from '@/types';

/**
 * Per-server health row: heartbeat dot, name, worker count + uptime, queues,
 * load bar. Load source preference: CPU% if reported, otherwise worker
 * utilization (busy / total). If neither is available the bar is omitted.
 */
export function ServerHealth() {
  const { data, isLoading } = useServers();

  const servers = useMemo(() => data ?? [], [data]);
  const totalWorkers = useMemo(
    () => servers.reduce((acc, s) => acc + s.workers.length, 0),
    [servers],
  );

  return (
    <Panel className="flex h-full flex-col px-3.5 py-3">
      <div className="mb-2 flex items-center justify-between">
        <span className="text-[13.5px] font-semibold">Servers</span>
        <span className="mono text-[11px] text-text-dim">
          {servers.length} nodes · {totalWorkers} workers
        </span>
      </div>
      <div className="flex-1 overflow-auto">
        {isLoading && servers.length === 0 && (
          <div className="text-[11.5px] text-text-mute">Loading…</div>
        )}
        {!isLoading && servers.length === 0 && (
          <div className="text-[11.5px] text-text-mute">No servers reporting.</div>
        )}
        {servers.map((s, i) => (
          <ServerRow key={s.id} server={s} isFirst={i === 0} isLast={i === servers.length - 1} />
        ))}
      </div>
    </Panel>
  );
}

type ServerRowProps = {
  server: ServerModel;
  isFirst: boolean;
  isLast: boolean;
};

function ServerRow({ server, isFirst, isLast }: ServerRowProps) {
  const fresh = isHeartbeatFresh(server.lastHeartbeatTime);
  const uptime = formatUptime(server.startedTime);
  const queues = collectQueues(server.workers);
  const busy = server.workers.filter((w) => w.currentJobId).length;
  const total = server.workers.length;

  const load = server.cpuUsagePercent != null
    ? { value: server.cpuUsagePercent / 100, label: `${Math.round(server.cpuUsagePercent)}%` }
    : total > 0
      ? { value: busy / total, label: `${busy}/${total}` }
      : null;

  return (
    <Link
      to={`/servers/${server.id}`}
      className={
        'block py-2 ' +
        (!isFirst ? 'border-t border-border ' : '') +
        (!isLast ? '' : '')
      }
    >
      <div className="flex items-center justify-between">
        <div className="flex min-w-0 items-center gap-2">
          <PulseDot colorClass={fresh ? 'text-warp-green' : 'text-warp-amber'} size={5} />
          <span className="mono truncate text-[12px] font-medium">{server.serverName}</span>
        </div>
        <span className="mono flex-shrink-0 text-[10.5px] text-text-mute">
          {total > 0 ? `${busy}/${total}` : '—'} · {uptime}
        </span>
      </div>
      {queues && (
        <div className="mono mb-1.5 mt-1 truncate text-[10.5px] text-text-mute">{queues}</div>
      )}
      {load && (
        <div className="flex items-center gap-2">
          <div className="h-1.5 flex-1 overflow-hidden rounded-sm bg-panel-2">
            <div
              className="h-full rounded-sm transition-[width]"
              style={{
                width: `${Math.min(100, Math.max(0, load.value * 100))}%`,
                background:
                  load.value > 0.85
                    ? 'var(--warp-red)'
                    : load.value > 0.7
                      ? 'var(--warp-amber)'
                      : 'var(--warp-green)',
              }}
            />
          </div>
          <span className="mono w-9 flex-shrink-0 text-right text-[10.5px] text-text-dim">
            {load.label}
          </span>
        </div>
      )}
    </Link>
  );
}

function isHeartbeatFresh(lastHeartbeatTime: string): boolean {
  const ms = Date.now() - new Date(lastHeartbeatTime).getTime();

  return ms < 30_000;
}

function formatUptime(startedTime: string): string {
  const ms = Date.now() - new Date(startedTime).getTime();
  const sec = Math.max(0, Math.floor(ms / 1000));
  const d = Math.floor(sec / 86400);
  const h = Math.floor((sec % 86400) / 3600);
  if (d > 0) {
    return `${d}d ${String(h).padStart(2, '0')}h`;
  }
  const m = Math.floor((sec % 3600) / 60);
  if (h > 0) {
    return `${h}h ${String(m).padStart(2, '0')}m`;
  }

  return `${m}m`;
}

function collectQueues(workers: WorkerModel[]): string | null {
  const set = new Set<string>();
  for (const w of workers) {
    if (w.queues) {
      for (const q of w.queues.split(',')) {
        const trimmed = q.trim();
        if (trimmed) {
          set.add(trimmed);
        }
      }
    }
  }
  if (set.size === 0) {
    return null;
  }

  return [...set].join(', ');
}
