using Jobly.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace Jobly.SqlServer;

internal sealed class SqlServerNotificationTransportFactory : IJoblyNotificationTransportFactory
{
    public IJoblyNotificationTransport Create(string connectionString, JoblyDatabasePushConfiguration options, ILoggerFactory loggerFactory)
    {
        return new SqlServerNotificationTransport(
            connectionString,
            options,
            loggerFactory.CreateLogger<SqlServerNotificationTransport>());
    }
}
