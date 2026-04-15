# Plan: Retry as External Pipeline Module

## Batch 1: Core Generic Infrastructure (3 files)

### 1.1 `Jobly.Core/Handlers/JobFailureOutcome.cs` (NEW)
POCO with `State`, `ScheduleTime`, `RetriedTimesIncrement`, `ClearHandlerType`.

### 1.2 `Jobly.Core/Handlers/IJobContext.cs`
Add `int RetriedTimes { get; }` and `JobFailureOutcome? FailureOutcome { get; set; }` to interface + impl.

### 1.3 `Jobly.Core/Publisher.cs`
Skip `$`-prefixed keys during parent metadata inheritance in `RunPublishPipeline`.

### Checkpoint: `dotnet build src/Jobly.sln`

## Batch 2: Retry Module (4 new files)

### 2.1 `Jobly.Worker/Retry/RetryOptions.cs` (NEW)
```csharp
public class RetryOptions { public int MaxRetries { get; set; } public int[] Delays { get; set; } = [15, 60, 300]; }
```

### 2.2 `Jobly.Worker/Retry/RetryPublishBehavior.cs` (NEW)
Open generic `IPublishPipelineBehavior<T>`. Injects `$maxRetries` and `$retryDelays` from `RetryOptions` (only if not already set by user pipeline).

### 2.3 `Jobly.Worker/Retry/RetryPipelineBehavior.cs` (NEW)
Open generic `IPipelineBehavior<TReq, TRes>`. Catches exceptions for IJob. Reads `$maxRetries`/`$retryDelays` from metadata via `IJobContext`. Sets `FailureOutcome`.

### 2.4 `Jobly.Worker/Retry/RetryServiceConfiguration.cs` (NEW)
`AddJoblyRetry(Action<RetryOptions>?)` extension method. Registers options + both behaviors.

### Checkpoint: `dotnet build src/Jobly.sln`

## Batch 3: Worker Refactoring (2 files)

### 3.1 `Jobly.Worker/JoblyWorkerService.cs`
- Set `jobContext.RetriedTimes = job.RetriedTimes` before handler execution
- Catch block: read `jobContext.FailureOutcome`, apply or default to Failed
- Remove retry if-block from `UpdateJobState`
- `UpdateJobState` no longer sets state — just finalizes (counters, logs, cleanup)
- Success path: set `job.CurrentState = State.Completed` before calling UpdateJobState

### 3.2 `Jobly.Worker/JoblyDispatcherWorker.cs`
Identical changes.

### Checkpoint: `dotnet build src/Jobly.sln`

## Batch 4: Tests (3 files)

### 4.1 Config updates
- `RetryTests.cs`: register `AddJoblyRetry` with `Delays = []` for existing tests
- `JoblyTestServer.cs`: register `AddJoblyRetry` with `Delays = [1]`

### 4.2 Unit tests
- `_WithRetryDelays_SetsScheduleTimeInFuture`
- `_LastDelayReusedWhenArrayShorter`
- `_WithEmptyRetryDelays_RetriesImmediately`
- `_WithPerJobRetryDelays_OverridesGlobalConfig`
- `_JobNotPickedUpBeforeDelay`
- `_WithMaxRetriesInMetadata_UsesMetadataValue`

### 4.3 Integration tests
- `_ThenScheduleTimeUpdatedOnRetry`
- `_ThenUsesPerJobDelays`

### Checkpoint: `dotnet test src/Jobly.sln --filter "Category!=SqlServer"`
