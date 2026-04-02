# Security Review

Completed April 2026. All critical/high items resolved or accepted.

## Resolved

- ~~Stale recovery ignores CancellationMode~~ — Fixed: respects cancel intent
- ~~Mutex race condition~~ — Fixed: FOR UPDATE blocking lock
- ~~Message routing no-handler logging~~ — Fixed: adds Failed log
- ~~RequeueJob on Processing~~ — Fixed: silently returns
- ~~Orphaned Awaiting children~~ — Fixed: OrchestrationTask cleans up
- ~~FK violation on expiration cleanup~~ — Fixed: skips parents with active children

## Accepted Risks

### LocalRequestsOnlyAuthorizationFilter and Reverse Proxies
**Severity:** MEDIUM — **Accepted with documentation**
`RemoteIpAddress` shows the proxy IP behind a reverse proxy. The localhost check passes/fails based on the proxy, not the client. Users behind proxies must configure `ForwardedHeadersMiddleware` in their app. Documented in dashboard-auth.md.

### CSRF on Login/Logout
**Severity:** LOW — **Accepted**
Login/logout endpoints lack CSRF tokens. Worst case: attacker can force logout from a malicious page. Cannot force login or access data. Impact is trivial for an internal dashboard tool.

### Cookie Timestamp Not Validated
**Severity:** LOW — **Accepted**
Cookie uses 7-day expiry via browser cookie expiration. Server-side timestamp validation would add complexity (sliding refresh on every request) for minimal security gain. Cookie is signed via ASP.NET Data Protection — forgery is not possible.

### Bulk Delete/Requeue Not Atomic
**Severity:** LOW — **By design**
Each job in a bulk operation has its own transaction. If the process dies mid-bulk, some jobs are affected and some aren't. Failures skip, don't propagate. Retry-safe due to idempotent operations.

### No Login Rate Limiting
**Severity:** LOW — **Accepted**
Built-in login is for dev/demo use. Production deployments should use `IJoblyAuthorizationFilter` with their app's own auth. Users who need rate limiting can implement it in their `IJoblyCredentialValidator` or add ASP.NET rate limiting middleware.
