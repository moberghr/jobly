# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Jobly

Jobly is a distributed job processing and message queue library for .NET 10. It provides two patterns:

- **Messages** (`IMessage`) — Pub/sub queue semantics. A message can have multiple handlers, each executed as an independent job.
- **Jobs** (`IJob`) — Orchestrated background work. Single handler, with scheduling, retries, continuations, and batch processing.

Ships as NuGet packages (Jobly.Core, Jobly.UI, Jobly.Worker) and supports both SQL Server and PostgreSQL.

## Build & Test Commands

```bash
# Backend (from src/)
dotnet build Jobly.sln
dotnet test Jobly.sln

# Run only PostgreSQL tests (faster, skips SQL Server container)
dotnet test Jobly.sln --filter "Category!=SqlServer"

# Run a specific test
dotnet test Jobly.sln --filter "FullyQualifiedName~GetAndProcessJobTests"

# Frontend (from src/ui/)
yarn install
yarn start          # Dev server on localhost:3000
yarn build          # Production build

# NuGet packaging
dotnet pack src/core/Jobly.Core -p:PackageVersion=1.0.0 --configuration Release
```

## Architecture

### Core Concepts

**Two publishing patterns:**
- `publisher.Publish(IMessage)` — Creates a Message row. Worker routes it by discovering all `IMessageHandler<T>` implementations and creating a Job per handler.
- `publisher.Enqueue(IJob)` / `publisher.Schedule(IJob, DateTime)` — Creates a Job row directly. Worker discovers the single `IJobHandler<T>` and executes it.

**Handler interfaces:**
```csharp
public interface IMessageHandler<in T> where T : IMessage { Task HandleAsync(T message, CancellationToken ct); }
public interface IJobHandler<in T> where T : IJob { Task HandleAsync(T message, CancellationToken ct); }
public interface IPipelineBehavior<in T> where T : class { Task HandleAsync(T message, JobHandlerDelegate next, CancellationToken ct); }
```

**Pipeline:** `IPipelineBehavior<T>` wraps all handler invocations (both Message and Job handlers) in a middleware chain.

### Data Model

- **Message** — Published intent (Type, Payload, Priority, State, JobCount). No scheduling.
- **Job** — Execution unit (Type, Message, HandlerType, MessageId, State, ScheduleTime, Priority, MaxRetries, RetriedTimes, ParentJobId, BatchId, CurrentWorkerId).
- **JobState** — State transition history for Jobs.
- **Batch** — Groups Jobs with a counter for batch completion tracking.
- **RecurringJob** — Cron-based scheduled Jobs.
- **Server** / **Worker** — Health monitoring. Server registered on startup with all workers. Workers track which job they're processing.

### Worker Flow

```
GetAndProcessJob():
  1. TryExecuteJob() — pick up Job (WHERE Enqueued AND ScheduleTime < now), run handler through pipeline
  2. TryRouteMessage() — pick up Message (WHERE Enqueued), discover handlers, create N Jobs, set JobCount
```

Jobs are preferred over Messages (real work before routing). Row locking (`FOR UPDATE SKIP LOCKED` / `UPDLOCK READPAST`) prevents duplicate processing.

### Backend (.NET 10)

The solution (`src/Jobly.sln`) is organized as:

- **Jobly.Core** — Entities, handler interfaces (`IJob`, `IMessage`, `IJobHandler`, `IMessageHandler`, `IPipelineBehavior`), `JobDispatcher`, `Publisher`, services. Generic over `TContext` (any DbContext).
- **Jobly.Worker** — `JoblyWorkerService` (polling worker), `JoblyWorkerSetup` (registers server + workers on startup), `JoblyHealthManager` (heartbeat + orphaned job cleanup).
- **Jobly.UI** — Razor-based dashboard UI with minimal API endpoints.

**DI registration:**
```csharp
services.AddJoblyWorker<TContext>(options => { options.WorkerCount = 5; });
services.AddJobHandlers(typeof(MyHandler).Assembly);
```

### Frontend (React 18 + TypeScript)

Located in `src/ui/`. Uses Zustand for state management, React Bootstrap for UI, Chart.js for real-time graphs, Axios for API calls. Currently pointed at a Postman mock — not connected to the real backend.

### Testing

66 integration tests using xUnit, Shouldly, and Testcontainers (PostgreSQL + SQL Server). Tests cover job processing, message routing, multi-handler fan-out, concurrency safety, priority ordering, scheduling, retries, continuations, batches, recurring jobs, and server monitoring. SQL Server tests can be skipped with `--filter "Category!=SqlServer"` for faster iteration.
