using Microsoft.Extensions.Logging;
using Npgsql;
using Warp.Core.Notifications;

namespace Warp.Provider.PostgreSql;

internal sealed class PostgresNotificationTransportFactory : IWarpNotificationTransportFactory
{
    private readonly NpgsqlDataSource? _dataSource;

    public PostgresNotificationTransportFactory()
    {
    }

    public PostgresNotificationTransportFactory(NpgsqlDataSource? dataSource)
    {
        _dataSource = dataSource;
    }

    public IWarpNotificationTransport Create(string connectionString, WarpDatabasePushConfiguration options, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<PostgresNotificationTransport>();

        if (_dataSource is not null)
        {
            return new PostgresNotificationTransport(_dataSource, options, logger);
        }

        return new PostgresNotificationTransport(connectionString, options, logger);
    }
}
