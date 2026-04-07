---
sidebar_position: 6
---

# Servers

Live server and worker status. Shows custom server name, worker count, start time, and heartbeat. Each worker shows its current job (clickable) or "Idle".

Configure a custom server name:

```csharp
builder.Services.AddJoblyWorker<AppDbContext>(options =>
{
    options.ServerName = "my-api-server";
});
```

import Screenshot from '@site/src/components/Screenshot';

<Screenshot light="/img/screenshots/08-servers.png" dark="/img/screenshots/08-servers-dark.png" alt="Servers" />

Click a server to see its detail page with worker groups and background task status:

<Screenshot light="/img/screenshots/15-server-detail.png" dark="/img/screenshots/15-server-detail-dark.png" alt="Server Detail" />
