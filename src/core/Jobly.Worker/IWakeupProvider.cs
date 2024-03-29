namespace Jobly.Worker;

public interface IWakeupProvider
{
    Task ListenForUpdatesNotifications(CancellationToken cancellationToken, Action action);
}