using Microsoft.Extensions.Logging;

namespace Jobly.Core.Notifications;

/// <summary>
/// Creates a provider-specific <see cref="IJoblyNotificationTransport"/>. Registered by provider
/// packages via <c>UsePostgreSql</c>/<c>UseSqlServer</c> so the worker-side <c>UseDatabasePush</c>
/// extension can wire push without knowing the provider.
/// </summary>
public interface IJoblyNotificationTransportFactory
{
    IJoblyNotificationTransport Create(string connectionString, JoblyDatabasePushConfiguration options, ILoggerFactory loggerFactory);
}
