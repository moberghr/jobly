# Lessons Learned

> This file captures patterns and mistakes discovered during AI-assisted development.
> It is read at the start of every `/project:moberg-implement` session.
> Commit this file — it is institutional memory for the team.

## 2026-04-09 — Naming conventions & schema support

- EF Core's `.ToTable(nameof(Entity))` via Fluent API sets the table name with `ConfigurationSource.Explicit`, which prevents `EFCore.NamingConventions` from transforming it (conventions have lower priority than explicit config). To let naming conventions work, don't call `.ToTable()` — let EF Core use the CLR type name as the default (convention-sourced).
- When Respawn's `TablesToIgnore` uses hardcoded table names, they break when naming conventions transform those names. Resolve table names from the EF model instead (`model.FindEntityType(typeof(Entity))!.GetTableName()`).
- Using `entity.Metadata.SetSchema(schema)` sets schema without re-pinning the table name — this is the correct way to assign schema while preserving naming convention freedom. Don't use `.ToTable(name, schema)` which sets both explicitly.
- `null`-coalescing (`??`) on config properties silently discards explicit `null` values. When a user sets `Schema = null` to opt out, `config?.Schema ?? "jobly"` ignores their intent. Use `config != null ? config.Schema : "jobly"` to distinguish "no config" from "config with null".
- The `SqlServerRowLockInterceptor` hardcoded table names for string replacement. Use regex against the `FROM [table] AS [alias]` pattern instead, so it works regardless of naming convention or schema.

## 2026-04-09 — Multi-server integration tests

- `IBatchPublisher.StartNew()` and `ContinueBatchWith()` do NOT auto-save. Always call `batchPublisher.SaveChangesAsync()` after batch operations. The publisher and batch publisher are separate DI scopes — `publisher.SaveChangesAsync()` does not save the batch publisher's changes.
- Batch continuations are nested batches (Kind=Batch with ParentId=originalBatchId), not direct children. When asserting batch structure, query continuation batch children separately.
- Don't assert that "both servers processed some jobs" in multi-server tests. Jobly provides no fairness guarantee — competitive fetch-and-lock means one server can win all fetches. Test correctness (no duplicates), not load distribution.
- Always await cleanup of CancellableRequest after `DeleteJob` — call `WaitForJobState(id, State.Deleted)` to ensure the handler exits before the next test runs.
