# Lessons Learned

> This file captures patterns and mistakes discovered during AI-assisted development.
> It is read at the start of every `/project:moberg-implement` session.
> Commit this file — it is institutional memory for the team.

## 2026-04-09 — Multi-server integration tests

- `IBatchPublisher.StartNew()` and `ContinueBatchWith()` do NOT auto-save. Always call `batchPublisher.SaveChangesAsync()` after batch operations. The publisher and batch publisher are separate DI scopes — `publisher.SaveChangesAsync()` does not save the batch publisher's changes.
- Batch continuations are nested batches (Kind=Batch with ParentId=originalBatchId), not direct children. When asserting batch structure, query continuation batch children separately.
- Don't assert that "both servers processed some jobs" in multi-server tests. Jobly provides no fairness guarantee — competitive fetch-and-lock means one server can win all fetches. Test correctness (no duplicates), not load distribution.
- Always await cleanup of CancellableRequest after `DeleteJob` — call `WaitForJobState(id, State.Deleted)` to ensure the handler exits before the next test runs.
