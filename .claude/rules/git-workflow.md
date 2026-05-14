# Git & Workflow

- **§7.1** Branch naming: hierarchical with `/` — `feat/multi-server-tests`, `fix/calculator-multiplication`, `chore/release-0.13.0`, `docs/0.12.0-release-notes`, `test/deterministic-concurrency`, `bug/setup`.
- **§7.2** Commit messages: imperative mood, describe the "what" concisely. Examples from recent history:
  - `Add realtime dashboard push (SignalR, opt-in addon)`
  - `feat: add IJobContext.ReportProgress for per-bar handler progress`
  - `fix: address 11 Dependabot alerts via transitive lockfile bumps`
  - `test: deterministic concurrency refactor + bounded PG pool`
- **§7.3** Static analyzers are enforced in build: StyleCop, Roslynator, SonarAnalyzer, Meziantou. All registered in `src/Directory.Build.props`. `TreatWarningsAsErrors=true` — **the build must pass with zero warnings**.
- **§7.4** `.editorconfig` enforces code style at the IDE level. Do not override severity levels in individual projects without a justified reason.
- **§7.5** **NEVER push to the remote without explicit approval**, even when CI is green. The engineer reviews the diff first. PRs are merged via GitHub UI after review.
- **§7.6** Never use `--no-verify` to skip pre-commit hooks. Never use `--force` to push to `main`. Investigate hook failures — fix the underlying issue.
