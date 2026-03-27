import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { formatRelativeTime, shortId } from '@/utils/format';
import type { ServerModel } from '@/types';
import * as api from '@/api';

export default function ServersPage() {
  const [servers, setServers] = useState<ServerModel[]>([]);

  useEffect(() => {
    api.getServers().then(setServers);
  }, []);

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
                    {server.serverName}
                  </CardTitle>
                  <div className="flex items-center gap-4 text-sm text-muted-foreground">
                    <span>{server.serviceCount} workers</span>
                    <span>Started {formatRelativeTime(server.startedTime)}</span>
                    <span>Heartbeat {formatRelativeTime(server.lastHeartbeatTime)}</span>
                  </div>
                </div>
              </CardHeader>
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
                          <TableCell className="font-mono text-xs">{shortId(w.workerId)}</TableCell>
                          <TableCell className="text-sm text-muted-foreground">
                            {formatRelativeTime(w.startedTime)}
                          </TableCell>
                          <TableCell>
                            {w.currentJobId ? (
                              <Link to={`/jobs/${w.currentJobId}`} className="text-primary hover:underline text-xs font-mono">
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
          ))}
        </div>
      )}
    </div>
  );
}
