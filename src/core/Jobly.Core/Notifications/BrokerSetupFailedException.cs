namespace Jobly.Core.Notifications;

/// <summary>
/// Thrown by <see cref="SqlServerNotificationTransport"/> when Service Broker setup
/// (ENABLE_BROKER, CREATE MESSAGE TYPE/CONTRACT/QUEUE/SERVICE) fails. The caller should
/// log clearly and fall back to polling — DB push is unavailable.
/// </summary>
public sealed class BrokerSetupFailedException : Exception
{
    public BrokerSetupFailedException()
    {
    }

    public BrokerSetupFailedException(string message)
        : base(message)
    {
    }

    public BrokerSetupFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
