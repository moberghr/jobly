---
sidebar_position: 8
---

# Dashboard Authorization

By default, the Jobly dashboard is open to everyone. Use `IJoblyAuthorizationFilter` to restrict access.

## Setup

```csharp
app.UseJoblyUI(options =>
{
    options.Authorization = new MyAuthFilter();
    options.UnauthorizedRedirectUrl = "/login"; // optional
});
```

## IJoblyAuthorizationFilter

Implement this interface to control who can access the dashboard:

```csharp
public class MyAuthFilter : IJoblyAuthorizationFilter
{
    public bool Authorize(HttpContext httpContext)
    {
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole("Admin");
    }
}
```

The filter is called for both the SPA (HTML/CSS/JS) and the API endpoints (`/jobly/api/...`).

## Redirect Behavior

When `UnauthorizedRedirectUrl` is set:
- **Browser requests** to `/jobly` get a 302 redirect to the login URL with `?returnUrl=/jobly`
- **API requests** (`/jobly/api/...`) always return 401 — no redirect

When `UnauthorizedRedirectUrl` is null:
- All unauthorized requests return 401

import Screenshot from '@site/src/components/Screenshot';

<Screenshot light="/img/screenshots/11-login.png" dark="/img/screenshots/11-login-dark.png" alt="Login" />

## Built-in Filters

### LocalRequestsOnlyAuthorizationFilter

Allows access only from localhost (127.0.0.1 / ::1):

```csharp
app.UseJoblyUI(options =>
{
    options.Authorization = new LocalRequestsOnlyAuthorizationFilter();
});
```

## Cookie Auth Example

If your app uses cookie authentication, the dashboard works automatically — cookies are sent with every request:

```csharp
// Program.cs
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// UseJoblyUI AFTER auth middleware so HttpContext.User is populated
app.UseJoblyUI(options =>
{
    options.Authorization = new AuthenticatedUserFilter();
    options.UnauthorizedRedirectUrl = "/login";
});
```

```csharp
public class AuthenticatedUserFilter : IJoblyAuthorizationFilter
{
    public bool Authorize(HttpContext httpContext)
    {
        return httpContext.User.Identity?.IsAuthenticated == true;
    }
}
```

:::important Pipeline Order
`UseJoblyUI()` must come **after** `UseAuthentication()` and `UseAuthorization()` so that `HttpContext.User` is populated when the filter runs.
:::
