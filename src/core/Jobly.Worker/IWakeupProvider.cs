using Jobly.Worker.Enums;

namespace Jobly.Worker;

public interface IWakeupProvider
{
    Task ListenForUpdatesNotifications(CancellationToken cancellationToken, Action<WakeupType> action);
}