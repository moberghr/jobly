# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is Jobly

Jobly is a distributed job queue/background job processing library for .NET 7 with a React TypeScript dashboard. It supports job scheduling, recurring jobs (cron), batch processing, retry logic, and real-time monitoring. It ships as NuGet packages (Jobly.Core, Jobly.UI, Jobly.Worker) and supports both SQL Server and PostgreSQL.

## Build & Test Commands

```bash
# Backend (from src/)
dotnet build Jobly.sln
dotnet test Jobly.sln

# Run a single test
dotnet test src/test/Jobly.Tests --filter "FullyQualifiedName~GetAndProcessJobTests"

# Frontend (from src/ui/)
yarn install
yarn start          # Dev server on localhost:3000
yarn build          # Production build
yarn test           # Tests in watch mode

# NuGet packaging
dotnet pack src/core/Jobly.Core -p:PackageVersion=1.0.0 --configuration Release
```

## Architecture

### Backend (.NET 7)

The solution (`src/Jobly.sln`) is organized as:

- **Jobly.Core** — Main library. Contains EFCore entities (Job, JobState, RecurringJob, Batch), service interfaces, and the publishing pipeline. All services are generic over `TContext` (any DbContext), registered via `ServiceConfiguration<TContext>`.
- **Jobly.Worker** — Hosted service that polls for and processes jobs. Configured with `services.AddJoblyWorker<TContext>(workerCount, retryCount)`.
- **Jobly.UI** — Razor-based UI components embedded in Core for the dashboard.

**Key interfaces:**
- `IPublisher<TContext>` — Enqueue jobs with optional scheduling, retries, and parent job linking
- `IRecurringJobPublisher<TContext>` — Manage cron-based recurring jobs via `AddOrUpdateRecurringJob<T>`
- `IJoblyService<TContext>` — Dashboard queries (job lists, counts, status)
- `IJoblyWorkerService<TContext>` — Job fetching and processing logic

**Database concurrency** uses EFCore interceptors:
- `RowLockInterceptor` — PostgreSQL uses `FOR NO KEY UPDATE SKIP LOCKED`; SQL Server uses `UPDLOCK READPAST`
- `SaveChangesConcurrencyTokenInterceptor` — Optimistic concurrency on RecurringJob updates

**Job state machine:** Enqueued → Awaiting → Processing → Completed/Failed/Deleted

### Frontend (React 18 + TypeScript)

Located in `src/ui/`. Uses Zustand for state management, React Bootstrap for UI, Chart.js for real-time graphs, and Axios for API calls.

**Prettier config:** 4-space indentation, 120 print width, trailing commas, semicolons.

### Testing

Tests use xUnit, Shouldly (assertions), Moq, and Testcontainers (real database instances for integration tests). Test projects: `Jobly.Tests`, `Jobly.Test.Shared`, `Jobly.TestApp`, `Jobly.TestWorker`.
