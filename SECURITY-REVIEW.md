# Security Review — Remaining Items

Items from the April 2026 audit that haven't been fixed yet.

## Security Hardening

### No CSRF Protection on Login/Logout Endpoints
**Severity:** HIGH
Login POST (`/api/auth/login`) and logout POST (`/api/auth/logout`) accept requests without CSRF tokens. A malicious page could force logout via cross-origin form submission.
**Fix:** Add a custom header requirement (e.g., `X-Jobly-Auth: 1`) that the React SPA sends. Browsers won't add custom headers from cross-origin form posts.

### No Rate Limiting on Login Endpoint
**Severity:** HIGH
Unlimited login attempts. With a weak password, brute force is trivial.
**Fix:** In-memory rate limiting — max 5 failed attempts per IP per minute. Reset on success.

### Cookie Timestamp Not Validated
**Severity:** MEDIUM
Cookie contains `DateTime.UtcNow` but the auth filter only checks `payload.StartsWith("jobly|")`. A stolen cookie works for the full 7 days even if the password changes.
**Fix:** Parse the timestamp from the cookie payload and reject cookies older than a configurable window (e.g., 24 hours). Or add a sliding refresh.

### LocalRequestsOnlyAuthorizationFilter and Reverse Proxies
**Severity:** MEDIUM
`RemoteIpAddress` always shows the proxy IP when behind a reverse proxy (Nginx, Azure, etc.). The localhost check passes/fails based on the proxy, not the client.
**Fix:** Document that `ForwardedHeadersMiddleware` must be configured for proxied setups. The filter checks the direct connection IP, which is correct when not proxied.

## Operational

### Bulk Delete/Requeue Not Atomic
**Severity:** LOW
Each job in a bulk operation has its own transaction. If the process dies mid-bulk, some jobs are affected and some aren't. No way to rollback. This is by design (failures skip, don't propagate) but should be documented.

### No Job Type Allowlist for Deserialization
**Severity:** LOW
Worker deserializes `Type.GetType(job.Type)` without validation. While JSON deserialization is generally safe, defense-in-depth could maintain an allowlist of types.
