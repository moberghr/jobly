using Jobly.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace Jobly.Provider.PostgreSql;

internal sealed class PostgresNotificationTransportFactory : IJoblyNotificationTransportFactory
{
    public IJoblyNotificationTransport Create(string connectionString, JoblyDatabasePushConfiguration options, ILoggerFactory loggerFactory)
    {
        return new PostgresNotificationTransport(
            connectionString,
            options,
            loggerFactory.CreateLogger<PostgresNotificationTransport>());
    }
}
