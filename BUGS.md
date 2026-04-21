# Known Bugs & Issues

## Bugs

### UI crash on null type
`shortType()` crashes with `Cannot read properties of null (reading 'split')` when a job has `type: null` (batch/continuation jobs). Need null check in `shortType()` utility.

### Expiration cleanup FK violation
`DELETE FROM Job` fails with `violates foreign key constraint "fk_job_job_parent_job_id"` when trying to delete a parent job whose children haven't been deleted yet. Cleanup must delete in dependency order (children first, then parents) or delete children of expired jobs in the same batch.

## Behavior Fixes

### Batch detail page doesn't show continuations
Batch detail page only shows the batch's children. Should also show the continuation batch (if any) linked via `ParentJobId`.

### Heartbeat task generates no logs
`LogOnSuccess => false` suppresses all ServerLog writes. Consider logging periodically (every Nth run) or when metrics change significantly.

## Enhancements

### Show handler type in all job lists
Handler type is only on `JobDetailModel`, not `JobModel`. Add it to `JobModel` and all Select projections so it shows in job list, batch detail jobs, message detail jobs, recurring job history, etc.

### JobDispatcher reflection caching
`DiscoverJobHandler` and `DiscoverMessageHandlers` use reflection on every call. Cache the results per type for better performance at scale.

### Database migration strategy
Currently uses `EnsureCreatedAsync()` which only works for fresh databases. Need a strategy for schema changes in production (EF migrations, SQL scripts, or manual ALTER TABLE).
