using Microsoft.Extensions.Logging;
using Warp.Core.Notifications;

namespace Warp.Provider.PostgreSql;

internal sealed class PostgresNotificationTransportFactory : IWarpNotificationTransportFactory
{
    public IWarpNotificationTransport Create(string connectionString, WarpDatabasePushConfiguration options, ILoggerFactory loggerFactory)
    {
        return new PostgresNotificationTransport(
            connectionString,
            options,
            loggerFactory.CreateLogger<PostgresNotificationTransport>());
    }
}
