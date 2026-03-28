# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Jobly

Jobly is a distributed job processing and message queue library for .NET 10. It provides two patterns:

- **Messages** (`IMessage`) — Pub/sub queue semantics. Multiple handlers per message, each executed as an independent job.
- **Jobs** (`IJob`) — Orchestrated background work. Single handler with scheduling, retries, continuations, batches, and named queues.

Ships as NuGet packages (Jobly.Core, Jobly.UI, Jobly.Worker). Supports PostgreSQL and SQL Server.

## Build & Test Commands

```bash
# Backend (from src/)
dotnet build Jobly.sln
dotnet test Jobly.sln --filter "Category!=SqlServer"   # PostgreSQL only (~9s with Respawn)
dotnet test Jobly.sln                                    # All databases

# Run a specific test
dotnet test Jobly.sln --filter "FullyQualifiedName~ConcurrencyTests"

# Frontend (from src/ui/)
npm install
npm run dev      # Vite dev server on localhost:5173 (proxies /api to localhost:5000)
npm run build    # Production build
```

## Architecture

### Two Publishing Patterns

```csharp
// Messages — pub/sub, multiple handlers, fan-out to independent jobs
publisher.Publish(new OrderCreated { OrderId = 123 });

// Jobs — single handler, scheduling, retries, continuations
publisher.Enqueue(new SendReport());
publisher.Schedule(new SendReport(), DateTime.UtcNow.AddHours(1));
publisher.Enqueue(new SendReport(), queue: "critical");
publisher.Enqueue(new FollowUp(), parentJobId: parentId);  // continuation
```

### Handler Interfaces

```csharp
public interface IMessageHandler<in T> where T : IMessage { Task HandleAsync(T message, CancellationToken ct); }
public interface IJobHandler<in T> where T : IJob { Task HandleAsync(T message, CancellationToken ct); }
public interface IPipelineBehavior<in T> where T : class { Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken ct); }
```

### Data Model

- **Message** — Published intent (Type, Payload, Queue, State, JobCount, ExpireAt). Pure queue, no scheduling.
- **Job** — Execution unit (Type, Message, HandlerType, MessageId, Queue, State, ScheduleTime, MaxRetries, RetriedTimes, ParentJobId, BatchId, CurrentWorkerId, ExpireAt).
- **JobState** — State transition history for Jobs.
- **JobLog** — ILogger output captured during handler execution.
- **Statistic** — Persistent counters (stats:succeeded, stats:failed, stats:deleted, stats:created) that survive job deletion.
- **Batch** — Groups Jobs with counter for batch completion tracking.
- **RecurringJob** — Cron-based scheduled Jobs.
- **Server** / **Worker** — Health monitoring, heartbeat, orphaned job recovery.

### Worker Flow

```
GetAndProcessJob():
  1. TryExecuteJob() — pick up Job (WHERE Enqueued AND Queue IN worker_queues AND ScheduleTime < now)
  2. TryRouteMessage() — pick up Message (WHERE Enqueued AND Queue IN worker_queues), discover handlers, create N Jobs
```

Jobs preferred over Messages (real work before routing). Row locking prevents duplicate processing. Queue order is alphabetical.

### Concurrency Safety

- All state-changing operations (DeleteJob, RequeueJob) use transaction + row lock (`FOR UPDATE`)
- Stats increments/decrements are inside the same transaction — atomic rollback if anything fails
- Bulk operations process each job in its own transaction — failures are skipped, not propagated
- Worker job pickup uses `SKIP LOCKED` to avoid contention

### Job Retention

- Completed/Deleted jobs: `ExpireAt = UtcNow + 1 day` (configurable)
- Failed jobs: `ExpireAt = null` (never auto-deleted)
- `JoblyHealthManager` periodically cleans up expired jobs/messages in batches
- Statistics persist after deletion (stats:succeeded, etc.)

### Backend (.NET 10)

- **Jobly.Core** — Entities, handler interfaces, JobDispatcher, Publisher, services, logging infrastructure. Generic over `TContext` (any DbContext).
- **Jobly.Worker** — `JoblyWorkerService` (polling worker), `JoblyWorkerSetup` (registers server + workers on startup), `JoblyHealthManager` (heartbeat + cleanup + expiration).
- **Jobly.UI** — Minimal API endpoints + embedded dashboard.

### Frontend (Vite + React 18 + TypeScript)

Located in `src/ui/`. Uses Tailwind + shadcn/ui for components, Zustand for state, Axios for API, Recharts for graphs.

**Features:** Dashboard with live/historical stats, job list by state with bulk actions, per-page selector (localStorage), dark mode, message list + detail, recurring jobs, servers with workers, job detail with state history + execution logs + flow visualization.

### Testing

108 integration tests using xUnit, Shouldly, Testcontainers + Respawn (single shared PostgreSQL container, data reset between tests). Tests cover: job processing, message routing, multi-handler fan-out, concurrency safety (row locking), queue ordering, scheduling, retries, continuations, batches, recurring jobs, server monitoring, log capture, job retention, statistics persistence, bulk operations, concurrent state changes.

SQL Server tests available but skipped by default (`--filter "Category!=SqlServer"`).
