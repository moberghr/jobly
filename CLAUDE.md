# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Jobly

Jobly is a distributed job processing and message queue library for .NET 10. Two patterns:

- **Messages** (`IMessage`) — Pub/sub queue. Multiple handlers per message, each an independent job.
- **Jobs** (`IJob`) — Orchestrated background work. Single handler, scheduling, retries, continuations, batches, named queues.

Ships as NuGet packages (Jobly.Core, Jobly.UI, Jobly.Worker). Supports PostgreSQL and SQL Server.

## Build & Test Commands

```bash
# Backend (from src/)
dotnet build Jobly.sln
dotnet test Jobly.sln --filter "Category!=SqlServer"   # PostgreSQL only (~10s with Respawn)
dotnet test Jobly.sln                                    # All databases

# Run a specific test
dotnet test Jobly.sln --filter "FullyQualifiedName~ConcurrencyTests"

# Frontend (from src/ui/)
npm install
npm run dev      # Vite dev server on localhost:5173 (proxies /api to localhost:5000)
npm run build    # Production build
```

## Architecture

### Publishing

```csharp
publisher.Publish(new OrderCreated());                    // IMessage → fan-out to N handlers
publisher.Enqueue(new SendReport());                      // IJob → single handler
publisher.Schedule(new SendReport(), tomorrow);           // IJob → scheduled
publisher.Enqueue(new Task(), queue: "critical");         // Named queue
publisher.Enqueue(new FollowUp(), parentJobId: id);       // Continuation
```

### Handler Interfaces

```csharp
public interface IMessageHandler<in T> where T : IMessage { Task HandleAsync(T message, CancellationToken ct); }
public interface IJobHandler<in T> where T : IJob { Task HandleAsync(T message, CancellationToken ct); }
public interface IPipelineBehavior<in T> where T : class { Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken ct); }
```

### Data Model

- **Message** — Published intent (Type, Payload, Queue, State, JobCount, ExpireAt).
- **Job** — Execution unit (Type, Message, HandlerType, MessageId, Queue, State, ScheduleTime, MaxRetries, RetriedTimes, ParentJobId, BatchId, CurrentWorkerId, ExpireAt).
- **JobLog** — Unified audit trail. EventType: Created, Processing, Completed, Failed, Requeued, Deleted, Log (ILogger output).
- **Statistic** — Persistent counters (totals + hourly time-series). Seeded via HasData. Atomic ExecuteUpdateAsync increments.
- **Batch** — Groups Jobs with counter for batch completion.
- **RecurringJob** — Cron-based scheduled Jobs.
- **Server** / **Worker** — Health monitoring, heartbeat, orphaned job recovery.

### Worker Flow

```
GetAndProcessJob():
  1. TryExecuteJob() — pick up Job (WHERE Enqueued AND Queue IN worker_queues AND ScheduleTime < now)
  2. TryRouteMessage() — pick up Message (WHERE Enqueued AND Queue IN worker_queues), discover handlers, create N Jobs
```

Queue order is alphabetical. Row locking (`FOR UPDATE SKIP LOCKED`) prevents duplicate processing. All state-changing operations use transaction + row lock for atomicity.

### Job Retention

- Completed/Deleted: ExpireAt = UtcNow + configurable TTL (default 1 day)
- Failed: ExpireAt = null (never auto-deleted)
- HealthManager cleans up expired jobs/messages + hourly stats older than 7 days
- Statistics persist after deletion

### Backend (.NET 10)

- **Jobly.Core** — Entities, handlers, JobDispatcher, Publisher, services, logging (JobLogContext/JobLoggerProvider).
- **Jobly.Worker** — JoblyWorkerService, JoblyWorkerSetup, JoblyHealthManager.
- **Jobly.UI** — Minimal API endpoints + dashboard.

### Frontend (Vite + React 18 + TypeScript)

`src/ui/`. Tailwind + shadcn/ui, Zustand, Axios, Recharts. Features: dashboard with realtime (jobs/sec) + historical (24h) graphs, job list by state with bulk actions, per-page selector, dark mode, Hangfire-style job detail (colored state cards + handler output), messages, recurring jobs, servers.

### Testing

120 integration tests using xUnit, Shouldly, Testcontainers + Respawn (~10s). Covers: lifecycle logs, concurrency (row locking), queue ordering, scheduling, retries, continuations, batches, recurring jobs, server monitoring, log capture, retention, statistics, bulk operations, time-series stats.

### Key Design Decisions

- Raw SQL acceptable for internal ops (stats, cleanup). EF integration matters for publishing (outbox pattern).
- If something can go wrong, assume it will. All state changes use transaction + row lock.
- Tests must call actual production code, never duplicate logic.
- Failed jobs never auto-deleted.
- Statistics use atomic ExecuteUpdateAsync with upsert for hourly keys.
