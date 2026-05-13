# Coding Style (Project-Specific Overrides)

> Full Moberg style guide: `.claude/references/coding-guidelines.md`.
> This file lists project-specific overrides and the rules most commonly violated in Warp.

- **§3.1** `var` for all local variables. No explicit types.
- **§3.2** Private fields: `_camelCase`. Public members: `PascalCase`. Interfaces: `IPascalCase`. Constants / static readonly: `PascalCase` (no underscore).
- **§3.3** Braces on all control flow (`if`, `else`, `while`, `for`, `foreach`) — even single-line bodies.
- **§3.4** File-scoped namespaces. One type per file unless a handler + request + response are grouped together by intent.
- **§3.5** Lambda parameter is `x`; nested lambdas use `y`, `z`.
- **§3.6** Split chained LINQ methods onto separate lines. Place `.` at the **start** of each line.

```csharp
var activeJobs = await _context.Set<Job>()
    .Where(x => x.CurrentState == State.Enqueued)
    .Where(x => x.ScheduleTime <= now)
    .Select(x =>
        new JobSummary
        {
            Id = x.Id,
            Type = x.Type,
        })
    .ToListAsync();
```

- **§3.7** Separate multiple `&&` conditions into multiple `.Where()` calls.
- **§3.8** Use `.Where()` before `.FirstOrDefault()` / `.SingleOrDefault()` — don't put predicates in the terminal method.
- **§3.9** Blank line before `return` statements. No double blank lines. Private methods last in file.
- **§3.10** Avoid `else` — return early (guard clauses).
- **§3.11** Use object initializers. Omit `()` in `new Type { ... }`.
- **§3.12** No `this.` prefix. No XML doc comments on internal code. Comments only for non-obvious *why*, never *what*.
- **§3.13** `using` directives outside namespace. `System.*` first, alphabetically sorted.
- **§3.14** Use simple `using var x = ...;` over `using (var x = ...) { }`.
- **§3.15** Prefer `??` over ternary null checks. Prefer ternary over `if/else` for simple assignments.
- **§3.16** Separate type members with a single blank line, except consecutive private fields (no blank line between them).
- **§3.17** Create variables close to where they're used, not at the top of the method.
- **§3.18** No helper lists — use `Select` or `yield return` instead.
- **§3.19** Place `new` keyword on a new line in `Select` projections, indented one level deeper than `Select`.
- **§3.20** Use `string.Equals(a, b, StringComparison.X)` instead of `==` for string comparison (enforced by MA0006).
