# Stryker.NET Mutation Testing

## Overview

Mutation testing for Jobly using a dedicated SQLite-only test project (`Jobly.Tests.Mutation`). No Docker required — runs 293 tests in ~10 seconds.

## How to run

```bash
cd src/tests/Jobly.Tests.Mutation

# Full Core run (~30 min)
dotnet stryker --test-runner mtp

# Single file (fast feedback, ~5 min)
dotnet stryker --test-runner mtp --mutate "**/Publisher.cs"

# Worker (~15 min)
dotnet stryker --test-runner mtp --config-file stryker-config.worker.json

# With baseline comparison (after baseline is established)
dotnet stryker --test-runner mtp --with-baseline
dotnet stryker --test-runner mtp --config-file stryker-config.worker.json --with-baseline
```

## Architecture

- **`Jobly.Tests.Mutation/`** — Separate test project with only SQLite fixture. No Testcontainers, no Docker.
- **`stryker-config.json`** — Core config. Mutates `Jobly.Core`, excludes entities/DTOs/interfaces.
- **`stryker-config.worker.json`** — Worker config. Mutates `Jobly.Worker`, lower thresholds.
- **`stryker.slnx`** — Mini solution for Stryker that excludes `Jobly.Tests` (prevents Docker container startup).
- **`SqliteFixture`** — In-memory SQLite, no row-lock interceptor (unit tests are single-threaded), `schema: null` (SQLite has no schema support).

## Baseline scores (2026-04-17)

| Project | Score | Killed | Survived | NoCoverage |
|---------|-------|--------|----------|------------|
| Core    | 99.60% | 743   | 3        | 253        |
| Worker  | 51.53% | 252   | 237      | 625        |

Worker score is low because `JoblyWorkerService` (the execution loop) and `ServerTaskBase` (polling infrastructure) are hard to unit test — they're covered by integration tests on PostgreSQL.

## Adding tests for new code

When adding a new test file to `Jobly.Tests`:
1. If it has `[Collection<PostgreSqlCollection>]`, create a corresponding `_Sqlite.cs` file in `Jobly.Tests.Mutation/Unit/`:

```csharp
using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class NewTests_Sqlite : NewTestsBase
{
    public NewTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
```

2. If the test uses `BeginTransactionAsync` (nested transactions), skip it — SQLite doesn't support them.

## Config details

### Excluded from mutation (Core)

Entities, DTOs, interfaces, enums, attributes — pure data containers where mutations are noise. See `mutate` array in `stryker-config.json`.

### Thresholds

| Project | Break | Low | High |
|---------|-------|-----|------|
| Core    | 60    | 70  | 80   |
| Worker  | 50    | 60  | 70   |

### Known limitations

- SQLite has no row-lock interceptor — concurrency tests are skipped
- SQLite has no schema support — `TestContext` uses `schema: null`
- SQLite doesn't support nested transactions — `OTelMetricsTests` excluded
- `--test-runner mtp` required (xUnit v3 uses Microsoft Testing Platform)
- Kill LSP (`csharp-ls`) before running if IDE has the worktree open — it locks `SourceGenerator.dll`

## Troubleshooting

### "SourceGenerator.dll is locked"
```bash
taskkill //F //PID $(tasklist | grep csharp-ls | awk '{print $2}')
```

### "Using Microsoft.Testing.Platform not supported"
Use `--test-runner mtp` flag. Don't use `vstest` — xUnit v3 is MTP-only.

### Stryker picks up Jobly.Tests (Docker containers)
The `stryker.slnx` mini solution excludes `Jobly.Tests`. Make sure the config has `"solution": "stryker.slnx"`.
