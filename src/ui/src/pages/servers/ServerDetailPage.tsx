import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Pagination } from '@/components/Pagination';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { shortId, formatBytes } from '@/utils/format';
import { ChevronDown, ChevronRight } from 'lucide-react';
import type { ServerModel, ServerTaskSummary, ServerLogModel, PagedList } from '@/types';
import * as api from '@/api';

const statusColors: Record<string, string> = {
  Completed: 'text-green-700 dark:text-green-400 bg-green-50 dark:bg-green-950/30',
  Failed: 'text-red-700 dark:text-red-400 bg-red-50 dark:bg-red-950/30',
  Skipped: 'text-yellow-700 dark:text-yellow-400 bg-yellow-50 dark:bg-yellow-950/30',
};

export default function ServerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [server, setServer] = useState<ServerModel | null>(null);
  const [tasks, setTasks] = useState<ServerTaskSummary[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (id) {
      api.getServerById(id).then(setServer).catch(() => setError('Unable to load server'));
      api.getServerTaskSummaries(id).then(setTasks).catch(() => {});
    }
  }, [id]);

  if (error) return <ErrorState message={error} />;
  if (!server) return <LoadingState />;

  return (
    <div>
      <div className="flex items-center gap-4 mb-6">
        <span className="inline-block w-3 h-3 rounded-full bg-green-500" />
        <h1 className="text-2xl font-bold">{server.serverName}</h1>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
        {/* Details */}
        <Card>
          <CardHeader className="pb-2"><CardTitle className="text-sm">Details</CardTitle></CardHeader>
          <CardContent className="space-y-2 text-sm">
            <div><span className="text-muted-foreground">Workers:</span> {server.serviceCount}</div>
            <div><span className="text-muted-foreground">CPU:</span> {server.cpuUsagePercent != null ? `${server.cpuUsagePercent}%` : 'N/A'}</div>
            <div><span className="text-muted-foreground">Memory:</span> {server.memoryWorkingSetBytes != null ? formatBytes(server.memoryWorkingSetBytes) : 'N/A'}</div>
            <div><span className="text-muted-foreground">Started:</span> <RelativeTime date={server.startedTime} /></div>
            <div><span className="text-muted-foreground">Heartbeat:</span> <RelativeTime date={server.lastHeartbeatTime} /></div>
            <div><span className="text-muted-foreground">ID:</span> <span className="font-mono text-xs">{server.id}</span></div>
          </CardContent>
        </Card>

        {/* Workers */}
        <Card>
          <CardHeader className="pb-2"><CardTitle className="text-sm">Workers ({server.workers.length})</CardTitle></CardHeader>
          <CardContent>
            {server.workers.length > 0 ? (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Worker ID</TableHead>
                    <TableHead>Started</TableHead>
                    <TableHead>Current Job</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {server.workers.map((w) => (
                    <TableRow key={w.workerId}>
                      <TableCell className="font-mono text-xs">
                        <Link to={`/workers/${w.workerId}`} className="text-primary hover:underline">
                          {shortId(w.workerId)}
                        </Link>
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        <RelativeTime date={w.startedTime} />
                      </TableCell>
                      <TableCell>
                        {w.currentJobId ? (
                          <Link to={`/jobs/detail/${w.currentJobId}`} className="text-primary hover:underline text-xs font-mono">
                            {shortId(w.currentJobId)}
                          </Link>
                        ) : (
                          <span className="text-muted-foreground text-sm">Idle</span>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            ) : (
              <p className="text-sm text-muted-foreground">No workers registered</p>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Task Sections */}
      <h2 className="text-lg font-semibold mb-3">Server Tasks</h2>
      {tasks.length === 0 ? (
        <Card>
          <CardContent className="py-6 text-center text-muted-foreground text-sm">No task logs yet</CardContent>
        </Card>
      ) : (
        <div className="space-y-2">
          {tasks.map((task) => (
            <TaskSection key={task.taskName} serverId={server.id} task={task} />
          ))}
        </div>
      )}
    </div>
  );
}

function TaskSection({ serverId, task }: { serverId: string; task: ServerTaskSummary }) {
  const [expanded, setExpanded] = useState(false);
  const [logs, setLogs] = useState<PagedList<ServerLogModel> | null>(null);
  const [page, setPage] = useState(0);

  const fetchLogs = useCallback(async () => {
    if (!expanded) return;
    try {
      const result = await api.getServerLogs(serverId, page, 10, task.taskName);
      setLogs(result);
    } catch {
      // Non-critical
    }
  }, [serverId, task.taskName, page, expanded]);

  useEffect(() => { fetchLogs(); }, [fetchLogs]);

  return (
    <Card>
      <button
        className="w-full text-left px-4 py-3 flex items-center justify-between hover:bg-accent/50 rounded-t-lg transition-colors"
        onClick={() => setExpanded(!expanded)}
      >
        <div className="flex items-center gap-3">
          {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
          <span className="font-medium text-sm">{task.taskName}</span>
          {task.lastStatus && (
            <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${statusColors[task.lastStatus] ?? 'text-muted-foreground'}`}>
              {task.lastStatus}
            </span>
          )}
        </div>
        <div className="flex items-center gap-4 text-xs text-muted-foreground">
          {task.intervalSeconds != null ? (
            <span>every {task.intervalSeconds}s</span>
          ) : (
            <span className="text-yellow-600 dark:text-yellow-400 font-medium">Disabled</span>
          )}
          {task.lastDurationMs != null && <span>took {task.lastDurationMs.toFixed(0)}ms</span>}
          {task.lastRun && <span>ran <RelativeTime date={task.lastRun} /></span>}
        </div>
      </button>

      {expanded && (
        <CardContent className="pt-0">
          {task.lastMessage && (
            <p className="text-sm text-muted-foreground mb-3">{task.lastMessage}</p>
          )}
          {logs && logs.items.length > 0 ? (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Status</TableHead>
                    <TableHead>Message</TableHead>
                    <TableHead>Duration</TableHead>
                    <TableHead>Time</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {logs.items.map((log) => (
                    <TableRow key={log.id}>
                      <TableCell>
                        <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${statusColors[log.status] ?? 'text-muted-foreground'}`}>
                          {log.status}
                        </span>
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground max-w-[300px] truncate">{log.message ?? '-'}</TableCell>
                      <TableCell className="text-sm text-muted-foreground">{log.durationMs != null ? `${log.durationMs.toFixed(0)}ms` : '-'}</TableCell>
                      <TableCell className="text-sm"><RelativeTime date={log.timestamp} /></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              {logs.pageCount > 1 && (
                <Pagination page={page} pageCount={logs.pageCount} onPageChange={setPage} />
              )}
            </>
          ) : (
            <p className="text-muted-foreground text-sm py-2 text-center">{logs ? 'No logs yet' : 'Loading...'}</p>
          )}
        </CardContent>
      )}
    </Card>
  );
}
