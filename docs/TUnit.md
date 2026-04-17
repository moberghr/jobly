# TUnit Evaluation (2026-04-15)

We evaluated migrating from xUnit v3 to TUnit v1.33.0. After a full side-by-side comparison with all 767 tests ported and passing, we decided **not to switch**. This document captures the findings for future reference.

## Why We Stayed on xUnit

- **No performance benefit** — test execution times were identical (~1m 33s for 767 tests). Both frameworks are dominated by Testcontainers startup and database I/O, not framework overhead.
- **Slower builds** — TUnit's source generator adds ~5s to clean builds (16s vs 11s).
- **Larger binaries** — test assembly 2.9 MB vs 1.1 MB; output directory 96 MB vs 51 MB.
- **More verbose syntax** — `[ClassDataSource<T>(Shared = SharedType.Keyed, Key = "...")]` + `[NotInParallel("...")]` vs xUnit's `[Collection<T>]`.
- **Required `[InheritsTests]`** on all 128 concrete subclasses — TUnit's source generator doesn't auto-discover inherited `[Test]` methods.
- **`[Retry]` name collision** — TUnit has its own `[Retry]` attribute that conflicts with Jobly's `[Retry]`.
- **Activity.Current interference** — TUnit creates an `Activity` per test for tracing, which broke 10 tests that assert on ambient `Activity.Current` state. Fixable with save/restore, but adds boilerplate.
- **TUnit's unique features (Native AOT, `[DependsOn]`, `[Retry]`, `[ParallelLimiter]`)** are not needed by this project.

## What We Did Instead

Added `TimedFactAttribute` / `TimedTheoryAttribute` with a default 10-second timeout — the one TUnit feature we wanted. See `src/core/Jobly.Tests/TestData/TimedFactAttribute.cs`.

## Benchmark Results

| Metric | xUnit v3 (3.2.2) | TUnit (1.33.0) |
|--------|:-:|:-:|
| Tests discovered | 767 | 767 |
| Tests passed | 767/767 | 767/767 |
| Build time (clean) | **11.3s** | 16.0s |
| Test execution (767 tests) | **~1m 34s** | ~1m 36s |
| Test discovery | **17.5s** | 31.5s |
| Test assembly size | **1.1 MB** | 2.9 MB |
| Output directory | **51 MB** | 96 MB |

## Migration Mapping Reference

If we revisit this in the future, here's the attribute mapping:

| xUnit | TUnit |
|-------|-------|
| `[Fact]` | `[Test]` |
| `[Theory]` | `[Test]` |
| `[InlineData(...)]` | `[Arguments(...)]` |
| `[Trait("k", "v")]` | `[Property("k", "v")]` |
| `[Collection<T>]` | `[ClassDataSource<T>(Shared = SharedType.Keyed, Key = "...")]` + `[NotInParallel("...")]` |
| `[CollectionDefinition]` + `ICollectionFixture<T>` | Not needed (delete) |
| `IAsyncLifetime` (fixtures) | `IAsyncInitializer` + `IAsyncDisposable` |
| `IAsyncLifetime` (test classes) | `[Before(HookType.Test)]` + `[After(HookType.Test)]` |
| Inherited tests from abstract base | Add `[InheritsTests]` to concrete class |
| `--filter-not-trait "Category=SqlServer"` | `-- --treenode-filter "/*/*/*/*[Category!=SqlServer]"` |

### Key Gotchas

1. **`IAsyncInitializer.InitializeAsync()`** returns `Task`, not `ValueTask` like xUnit's `IAsyncLifetime`.
2. **TUnit runs everything in parallel by default**, including tests sharing a fixture. Must add `[NotInParallel("key")]` to prevent Respawn corruption.
3. **TUnit creates an `Activity` per test** — tests asserting `Activity.Current == null` need `Activity.Current = null` save/restore.
4. **TUnitMigrator CLI tool** requires Central Package Management — didn't work for us.
5. **TUnit produces HTML test reports automatically** — one nice feature we'd lose by staying on xUnit.
