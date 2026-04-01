---
sidebar_position: 5
---

# Recurring Jobs

Cron-based scheduled jobs with name, cron expression, type, next/last execution times.

## How Recurring Jobs Work

`AddOrUpdateRecurringJob` only registers (or updates) the recurring job definition — it does **not** create any job instances. The `RecurringJobSchedulerTask` background task monitors all definitions and creates a new job each time the cron expression fires.

## Execution History

Each recurring job tracks its executions via `RecurringJobLog` entries. The history table shows the outcome of each scheduled run. If the underlying job has been deleted, the history entry displays **"Cleaned up"** instead of linking to the job.

## Dashboard Actions

- **Trigger** — immediately creates and enqueues a new job instance, regardless of the cron schedule
- **Delete** — removes the recurring job definition (existing job instances are not affected)

For full documentation on configuring and using recurring jobs, see [Recurring Jobs](../features/recurring-jobs.md).

![Recurring Jobs](/img/screenshots/07-recurring.png)
