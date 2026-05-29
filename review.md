# MTK setup-bootstrap eval — adversarial review (Moberg.Warp)

> Reviewer: fresh, anti-anchored. Judged generated artifacts against CODE ONLY.
> Did NOT read `run-report.md`, `.mtk-eval-orig/`, or `oracle/`.
> Target: `/Users/mirkobudimir/Dev/mtk-repos-pr/warp` @ `feat/mtk-setup-v7.10.0`.

## Findings

| # | artifact:line | claim (quoted) | ground-truth (with evidence) | category | severity |
|---|---|---|---|---|---|
| 1 | architecture-principles.md:27 | "the two source-generator projects are `netstandard2.0` … `Warp.SourceGenerator` and `Warp.Http.SourceGenerator` are `netstandard2.0`, all others `net10.0`" | FALSE. **Three** projects target `netstandard2.0`: those two PLUS `src/tests/Warp.Tests.SourceGenerator/Warp.Tests.SourceGenerator.csproj:4` (`<TargetFramework>netstandard2.0</TargetFramework>`, `<IsRoslynComponent>true</IsRoslynComponent>`, contains `DatabaseTestsGenerator.cs` = an `[Generator]`/`IIncrementalGenerator`). `grep -rl netstandard2.0 --include=*.csproj src` → 5 hits; the 3 with a `<TargetFramework>netstandard2.0</TargetFramework>` are the 3 generators. So "all others net10.0" is wrong. | FACTUAL_ERROR | MEDIUM |
| 2 | rules/architecture.md:§2.3 | "Source-generator projects target `netstandard2.0`; everything else targets `net10.0`. Don't change a generator project's TFM to net10.0." | The first clause is correct (all generator projects ARE netstandard2.0, incl. the un-enumerated `Warp.Tests.SourceGenerator`). The rule's *intent* (don't move a generator to net10.0) is correct and would actually protect `Warp.Tests.SourceGenerator`. But it pairs with the count error in #1/#3 — an agent reconciling "2 generators" against a 3rd netstandard2.0 project could "fix" `Warp.Tests.SourceGenerator` to net10.0 (it is mis-labeled as a "test" project), breaking the Roslyn component load. | WEAK_CLAIM | MEDIUM |
| 3 | CLAUDE.md:50 ; project-specific.md:§9.2 ; CLAUDE.md:§0.4 | ".NET 10 (libraries) + netstandard2.0 (**2 source generators**)" / "**Two** `netstandard2.0` source-generator projects (`Warp.SourceGenerator`, `Warp.Http.SourceGenerator`)" | Undercount. There are **3** netstandard2.0 Roslyn-component projects; the 3rd is `src/tests/Warp.Tests.SourceGenerator` (`DatabaseTestsGenerator.cs`, `IsRoslynComponent=true`). The "2 generators" framing is correct *only for `src/core`*; the artifacts state it as a repo-wide absolute. | FACTUAL_ERROR | MEDIUM |
| 4 | architecture-principles.md:28 | "17 projects: 8 in `src/core` (incl. 2 providers), **3 tests**, 3 benchmarks, 3 demo." | Project counts are exactly right (17 = 8 core + 3 tests + 3 bench + 3 demo, all verified). BUT one of the "3 tests" — `Warp.Tests.SourceGenerator` — is NOT an xUnit test project (no xunit ref, `IsRoslynComponent=true`, contains a `[Generator]`). Only `Warp.Tests` and `Warp.Tests.Mutation` are real xUnit projects (`xunit.v3.mtp-v2` 3.2.2). Mild mislabel: a Roslyn generator counted as a "test". | WEAK_CLAIM | LOW |
| 5 | architecture-principles.md:61 ; rules/data-layer.md:§5.2 | "snake_case DB naming via `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention`)" presented as a framework data-layer fact | Accurate but imprecise on ownership: `EFCore.NamingConventions` is referenced only in demo/test/benchmark csproj (`Warp.Test.Shared`, `Warp.Tests`, `Warp.ServerBenchmarks`, `Warp.PerfTest`), NOT in `Warp.Core` or the providers. snake_case is applied by the *consumer's* DbContext config; `WarpJobTableNames.cs:11` honors whatever the consumer chose. Cited evidence (`Test.Shared/ServiceConfiguration.cs:16`, `WarpJobTableNames.cs:11`) is correct. | WEAK_CLAIM | LOW |

## Claims verified TRUE (high-signal spot checks)

- **Framework-self / Warp-vs-MediatR (§0.1, §9.1, §9.5, arch 3.1):** `IJob`, `IMediator`, `IRequest`, `IMessage`, `IStreamRequest`, `IRequestHandler` all defined in `src/core/Warp.Core/Handlers/`. `WarpGeneratedHandlerRegistry.cs` present. `MediatR` appears in exactly one csproj: `src/benchmarks/Warp.Benchmarks/`. ✓ Correct, and the central distinction is captured well.
- **Raw SQL (§0.2, §5.1, arch 5):** `IWarpSqlQueries.cs:15-51` documents `FOR UPDATE SKIP LOCKED` (PG) / `FOR NO KEY UPDATE SKIP LOCKED` / `WITH (ROWLOCK,UPDLOCK,READPAST)` (MSSQL); `PostgresWarpSqlQueries.cs:55,77` contains the live SQL. Line range and "intentional, do not refactor" framing accurate. ✓
- **UI versions (§9.3, arch 9):** `src/ui/package.json` — `react ^19.2.4`, `vite ^8.0.1`, `@tanstack/react-query`, `@tanstack/react-table`, `axios ^1.13.6`, `@microsoft/signalr ^9.0.6`, `zustand ^5.0.12`, `tailwindcss ^4.2.2`, `shadcn ^4.1.1`, `typescript ~5.9.3`. Every quoted version is exact. ✓ (Round-1 version-claim risk did NOT recur.)
- **Project count:** `find src -name '*.csproj' … | wc -l` = 17; 8 core / 3 tests / 3 bench / 3 demo. ✓ (Round-1's 18-vs-17 error is FIXED.)
- **Test stack (§4, arch 6):** `xunit.v3.mtp-v2` 3.2.2, Shouldly, Moq, Respawn, Testcontainers (PG+MsSql) all in `Warp.Tests.csproj`; `SharedPostgreSqlContainer.cs`/`SharedSqlServerContainer.cs` exist; `Warp.Tests.Mutation` uses `SqliteFixture.cs`; InMemory referenced. `global.json` → `Microsoft.Testing.Platform`. ✓
- **Analyzer/build hygiene (§8.3, arch 7/8):** `Directory.Build.props` — StyleCop 1.2.0-beta.556, Roslynator 4.15.0, SonarAnalyzer.CSharp 10.22.x, Meziantou 3.0.29 (all `PrivateAssets=all`), `TreatWarningsAsErrors=true`, `NuGetAuditMode=all`, `AnalysisLevel=latest-recommended`, `PackageTags` quote exact. ✓
- **Patterns:** Cronos (Warp.Core.csproj) + `RecurringJobPublisher.cs`/`RecurringJobScheduler.cs`; `DistributedLock.Postgres`/`DistributedLock.SqlServer`; SignalR `WarpDashboardHub.cs`/`DashboardBroadcaster.cs`; `WarpHttpAttribute.cs`/`WarpHttpGenerator.cs`; no `*Controller.cs` in `src/core` (minimal API); `WarpEndpoints.cs`/`EndpointRouteBuilderExtensions.cs`; Swashbuckle in demo. ✓
- **Conventions (conventions.md):** "610/610 file-scoped namespaces, 0 block-scoped" — verified (the 5 namespace-less files are top-level `Program.cs`, correctly excluded). DI grouped in `*ServiceConfiguration.cs`/`Warp*Extensions.cs` (16 such files). "No repo-wide `Result<T>` envelope" — AMBIGUOUS tag honest. ✓
- **Confidence tags:** EXTRACTED claims are directly observable; the lone `[INFERRED:0.9]` (source-gen wiring) and `[AMBIGUOUS]` (partial Result pattern) are appropriately hedged. Tagging discipline is good. The audit even self-discloses that no tree-sitter/LSP symbol graph was harvested (arch:96-99) — honest about evidence basis.

## Summary

**Total claims checked:** ~45 concrete claims across CLAUDE.md, architecture-principles.md, conventions.md, 7 rules files, pre-commit-review-list.md.

**Counts by category:**
- FACTUAL_ERROR: 2 (findings #1, #3 — same root cause: netstandard2.0 generator undercount)
- WEAK_CLAIM: 3 (#2, #4, #5)
- HALLUCINATION: 0
- MISSING: 0
- OVERREACH/DILUTION: 0

**Counts by severity:** BLOCKING: 0 · MEDIUM: 3 · LOW: 2

**Most dangerous finding:** #1/#3 — the artifacts repeatedly assert exactly "2 source-generator (netstandard2.0) projects, everything else net10.0", but `src/tests/Warp.Tests.SourceGenerator` is a 3rd netstandard2.0 Roslyn component mislabeled as a test. The TFM rule (§2.3) "don't move a generator to net10.0" actually protects it, which is why this stays MEDIUM rather than BLOCKING — but the count is factually wrong and the "Tests" name invites an agent to mis-handle it.

**Top 3 dangerous findings:**
1. `arch:27` / `CLAUDE.md:50` / `§9.2` — "2 source generators / all others net10.0" is false; `Warp.Tests.SourceGenerator` is a 3rd netstandard2.0 generator (`IsRoslynComponent=true`, `DatabaseTestsGenerator.cs`).
2. `arch:28` — `Warp.Tests.SourceGenerator` counted as one of "3 tests" but it is a Roslyn generator, not an xUnit project.
3. `arch:61`/`§5.2` — snake_case framed as a Warp.Core data-layer fact; `EFCore.NamingConventions` is actually consumer-side (only in demo/test/bench csproj), not in Core/providers.

## Verdict

**PASS — no BLOCKING findings.** The generated artifacts are factually accurate for this repo on every load-bearing dimension an implementer would rely on: the Warp-vs-MediatR distinction, the intentional `FOR UPDATE SKIP LOCKED` raw SQL, dual-provider isolation, source-generated dispatch, test stack, analyzer/build hygiene, and all UI versions. Round-1 regressions (18-vs-17 project count; version drift) did NOT recur. The single real defect is a consistent undercount of netstandard2.0 source-generator projects (2 stated vs 3 actual), driven by the `Warp.Tests.SourceGenerator` project being mislabeled as a test; this is MEDIUM, not BLOCKING, because the governing TFM rule still steers an agent correctly. Recommend a one-line fix to the count claims before merge.

review.md written to `/Users/mirkobudimir/Dev/mtk-repos-pr/warp/review.md`.
