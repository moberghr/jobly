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

![Servers](/img/screenshots/08-servers.png)
