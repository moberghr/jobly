using Microsoft.Extensions.Logging;
using Warp.Core.Notifications;

namespace Warp.Provider.SqlServer;

internal sealed class SqlServerNotificationTransportFactory : IWarpNotificationTransportFactory
{
    public IWarpNotificationTransport Create(string connectionString, WarpDatabasePushConfiguration options, ILoggerFactory loggerFactory)
    {
        return new SqlServerNotificationTransport(
            connectionString,
            options,
            loggerFactory.CreateLogger<SqlServerNotificationTransport>());
    }
}
