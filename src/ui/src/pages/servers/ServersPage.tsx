import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { formatBytes } from '@/utils/format';
import { RelativeTime } from '@/components/RelativeTime';
import { LoadingState, ErrorState } from '@/components/PageState';
import { useRefreshKey } from '@/hooks/useRefreshKey';
import type { ServerModel } from '@/types';
import * as api from '@/api';

export default function ServersPage() {
  const [servers, setServers] = useState<ServerModel[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const refreshKey = useRefreshKey();

  useEffect(() => {
    api.getServers().then(setServers).catch(() => setError('Unable to load servers'));
  }, [refreshKey]);

  if (error) return <ErrorState message={error} />;
  if (!servers) return <LoadingState />;

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
                    <span className="inline-block w-2 h-2 rounded-full bg-green-500" />
                    <Link to={`/servers/${server.id}`} className="text-primary hover:underline">
                      {server.serverName}
                    </Link>
                  </CardTitle>
                  <div className="flex items-center gap-4 text-sm text-muted-foreground">
                    <span>{server.serviceCount} workers</span>
                    <span>CPU: {server.cpuUsagePercent != null ? `${server.cpuUsagePercent}%` : 'N/A'}</span>
                    <span>Mem: {server.memoryWorkingSetBytes != null ? formatBytes(server.memoryWorkingSetBytes) : 'N/A'}</span>
                    <span>Started <RelativeTime date={server.startedTime} /></span>
                    <span>Heartbeat <RelativeTime date={server.lastHeartbeatTime} /></span>
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
