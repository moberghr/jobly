# Quick Check List

> Project-specific inline verification. Read by moberg-implement and moberg-fix after each batch.
> Curate this list — add checks for patterns your team cares about, remove irrelevant ones.

- [ ] `var` for all local variables — no explicit types
- [ ] Braces on all control flow (`if`, `else`, `while`, `for`, `foreach`) — even single-line
- [ ] No `_context.Set<>()` subqueries inside `.Select()` — use nav properties or two-step fetch
- [ ] `AsNoTracking()` on read-only queries; `Select()` over `Include()`
- [ ] `TimeProvider` in production code, never `DateTime.UtcNow` (test code exempt)
- [ ] `CancellationToken` propagated through async call chains
- [ ] Lambda param is `x` (nested: `y`, `z`); LINQ chains split one method per line
- [ ] Blank line before `return`; no double blank lines; private methods last in file
- [ ] Tests added for every new/changed public method — one method per test, both PG + SQL Server subclasses
- [ ] No secrets, connection strings, or PII in logged output
