import { Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { formatBytes, serverStatusDotColor, isServerStale } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { useServers, usePauseServer, useResumeServer } from '@/api/hooks/useServers';
import { Pause, Play } from 'lucide-react';
import type { ServerModel } from '@/types';

export default function ServersPage() {
  const { data: servers, isLoading, isError } = useServers();
  const pauseServer = usePauseServer();
  const resumeServer = useResumeServer();

  const handleTogglePause = (server: ServerModel) => {
    if (server.pausedAt) {
      resumeServer.mutate(server.id);
    } else {
      pauseServer.mutate(server.id);
    }
  };

  if (isError) return <ErrorState message="Unable to load servers" />;
  if (isLoading || !servers) return <LoadingState />;

  return (
    <div>
      <h1 className="text-2xl font-bold mb-4">Servers</h1>

      {servers.length === 0 ? (
        <Card>
          <CardContent className="py-8 text-center text-muted-foreground">
            No servers connected
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-4">
          {servers.map((server) => (
            <Card key={server.id}>
              <CardHeader className="pb-2">
                <div className="flex items-center justify-between">
                  <CardTitle className="text-base flex items-center gap-2">
                    <span className={`inline-block w-2 h-2 rounded-full ${serverStatusDotColor(server.lastHeartbeatTime, server.pausedAt)}`} />
                    <Link to={`/servers/${server.id}`} className="text-primary hover:underline">
                      {server.serverName}
                    </Link>
                    {server.pausedAt && <Badge variant="outline" className="text-amber-600 border-amber-300">Paused</Badge>}
                    {isServerStale(server.lastHeartbeatTime) && <Badge variant="outline" className="text-red-600 border-red-300">Inactive</Badge>}
                  </CardTitle>
                  <div className="flex items-center gap-4 text-sm text-muted-foreground">
                    <span>{server.serviceCount} workers</span>
                    <span>CPU: {server.cpuUsagePercent != null ? `${server.cpuUsagePercent}%` : 'N/A'}</span>
                    <span>Mem: {server.memoryWorkingSetBytes != null ? formatBytes(server.memoryWorkingSetBytes) : 'N/A'}</span>
                    <span>Started <RelativeTime date={server.startedTime} /></span>
                    <span>Heartbeat <RelativeTime date={server.lastHeartbeatTime} /></span>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={(e) => { e.preventDefault(); handleTogglePause(server); }}
                      title={server.pausedAt ? 'Resume server' : 'Pause server'}
                      disabled={pauseServer.isPending || resumeServer.isPending}
                    >
                      {server.pausedAt ? <Play className="h-4 w-4" /> : <Pause className="h-4 w-4" />}
                    </Button>
                  </div>
                </div>
              </CardHeader>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
