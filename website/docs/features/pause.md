---
sidebar_position: 6
---

# Pause / Resume

Pause and resume job processing at the server or worker group level. Paused servers and worker groups stop picking up new jobs, but jobs already in progress continue to completion.

## Pause a Server

Pausing a server stops all workers on that server from picking up new jobs:

```
POST /api/servers/{serverId}/pause
POST /api/servers/{serverId}/resume
```

From the dashboard, use the pause/resume buttons on the server detail page.

## Pause a Worker Group

For finer control, pause individual worker groups. Other groups on the same server continue processing:

```
POST /api/groups/{groupId}/pause
POST /api/groups/{groupId}/resume
```

## How It Works

1. **Pause request** sets `PausedAt` timestamp on the server or worker group
2. **HeartbeatTask** (runs every ~3s) reads pause state from the database and updates an in-memory `PauseStateHolder`
3. **Workers** check the `PauseStateHolder` before each job poll — if paused, they skip the poll
4. **Resume** clears `PausedAt`, next heartbeat propagates the change, workers resume polling

The pause state is propagated via heartbeat, so there may be a delay of a few seconds between issuing the pause command and workers actually stopping.

## Behavior

| Scenario | What happens |
|----------|-------------|
| Pause server | All worker groups on that server stop polling |
| Pause worker group | Only that group stops polling; other groups continue |
| Job already processing | Continues to completion — pause doesn't cancel running handlers |
| Resume | Workers start polling again on next heartbeat cycle |

## Use Cases

- **Deployment** — Pause workers before deploying new handler code, resume after
- **Incident response** — Stop processing a specific queue while investigating an issue
- **Resource management** — Temporarily pause low-priority worker groups during peak load
- **Database maintenance** — Pause all servers before running migrations
