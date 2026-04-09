---
sidebar_position: 2
---

# Recurring Jobs

Recurring jobs execute on a cron schedule. Jobly handles scheduling, deduplication, and execution history.

## Register a Recurring Job

```csharp
await recurringPublisher.AddOrUpdateRecurringJob(
    new CleanupSessions(),
    name: "session-cleanup",
    cron: "0 * * * *");  // Every hour
```

`AddOrUpdateRecurringJob` only registers (or updates) the definition — it does **not** create a job. The `RecurringJobSchedulerTask` background task creates jobs when the cron time arrives.

## How It Works

1. **Registration**: `AddOrUpdateRecurringJob` stores the cron expression, message payload, and type. Sets `NextExecution` to the next cron occurrence.
2. **Scheduling**: `RecurringJobSchedulerTask` polls every 15 seconds. When `NextExecution <= now`, it creates a job with `ScheduleTime = now` (ready for immediate execution) and updates `NextExecution` to the next cron occurrence.
3. **Deduplication**: Before creating a new job, the scheduler checks the most recent `RecurringJobLog` entry. If that job is still `Enqueued` or `Processing`, it skips — no duplicate jobs.
4. **Execution**: The created job is a regular job. Workers pick it up, execute the handler, and it follows the normal lifecycle.

## Execution History

Each job created by the scheduler is logged in `RecurringJobLog`. The dashboard shows execution history on the recurring job detail page — including jobs that have been cleaned up (shown as "Cleaned up").

The `RecurringJobLog` has a FK to `Job` with `SET NULL` cascade. When a job expires and is cleaned up, the log entry survives with `JobId = null`. The last 100 entries per recurring job are retained.

## Cron Expressions

Standard 5-part cron (minute, hour, day, month, weekday) and 6-part with seconds:

```
* * * * *       Every minute
0 * * * *       Every hour
0 9 * * *       Daily at 9 AM
0 0 * * 1       Every Monday at midnight
*/5 * * * *     Every 5 minutes
0 9 * * 1-5     Weekdays at 9 AM
```

## Manual Trigger

Trigger a recurring job immediately from the dashboard or via the API:

```csharp
var svc = serviceProvider.GetRequiredService<IRecurringJobService>();
await svc.TriggerRecurringJob(id);
```

## Updating a Recurring Job

Call `AddOrUpdateRecurringJob` again with the same name. The cron expression, payload, and type are updated. `NextExecution` is recalculated.

```csharp
// Change from hourly to every 30 minutes
await recurringPublisher.AddOrUpdateRecurringJob(
    new CleanupSessions(),
    name: "session-cleanup",
    cron: "*/30 * * * *");
```

## Deleting a Recurring Job

```csharp
await recurringJobService.DeleteRecurringJob(id);
```

Or use the delete button on the dashboard.
