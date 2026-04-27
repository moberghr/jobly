using Microsoft.Extensions.Logging;

namespace Warp.Core.Notifications;

/// <summary>
/// Creates a provider-specific <see cref="IWarpNotificationTransport"/>. Registered by provider
/// packages via <c>UsePostgreSql</c>/<c>UseSqlServer</c> so the worker-side <c>UseDatabasePush</c>
/// extension can wire push without knowing the provider.
/// </summary>
public interface IWarpNotificationTransportFactory
{
    IWarpNotificationTransport Create(string connectionString, WarpDatabasePushConfiguration options, ILoggerFactory loggerFactory);
}
