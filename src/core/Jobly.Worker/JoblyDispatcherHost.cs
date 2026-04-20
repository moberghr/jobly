using Jobly.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Hosted service that constructs and manages the lifecycle of the dispatcher +
/// dispatcher-workers when <see cref="JoblyWorkerConfiguration.UseDispatcher"/> is true.
/// Depends on <see cref="ServerRegistrationState"/> having been populated by
/// <see cref="JoblyServerRegistration{TContext}"/>, which is registered first.
/// No-ops when dispatcher mode is disabled.
/// </summary>
public class JoblyDispatcherHost<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IOptions<JoblyWorkerConfiguration> _configurationOptions;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly IJoblyNotificationTransport _notificationTransport;
    private readonly ServerRegistrationState _state;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<BackgroundService> _workers = [];

    public JoblyDispatcherHost(
        IOptions<JoblyWorkerConfiguration> configuration,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider,
        PauseStateHolder pauseStateHolder,
        IJoblyNotificationTransport notificationTransport,
        ServerRegistrationState state,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration.Value;
        _configurationOptions = configuration;
        _serviceScopeFactory = serviceScopeFactory;
        _timeProvider = timeProvider;
        _pauseStateHolder = pauseStateHolder;
        _notificationTransport = notificationTransport;
        _state = state;
        _loggerFactory = loggerFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.UseDispatcher)
        {
            return;
        }

        foreach (var registration in _state.Groups)
        {
            var dispatcher = new JoblyDispatcher<TContext>(
                _serviceScopeFactory,
                _loggerFactory.CreateLogger<JoblyDispatcher<TContext>>(),
                _configurationOptions,
                registration.Config,
                _timeProvider,
                _pauseStateHolder,
                registration.GroupEntityId);

            await dispatcher.StartAsync(cancellationToken);
            _workers.Add(dispatcher);

            foreach (var workerId in registration.WorkerIds)
            {
                var worker = new JoblyDispatcherWorker<TContext>(
                    workerId,
                    dispatcher.JobReader,
                    _serviceScopeFactory,
                    _loggerFactory.CreateLogger<JoblyDispatcherWorker<TContext>>(),
                    _configurationOptions,
                    _timeProvider,
                    _notificationTransport);

                await worker.StartAsync(cancellationToken);
                _workers.Add(worker);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var tasks = _workers.Select(worker => worker.StopAsync(cancellationToken));
        await Task.WhenAll(tasks);
    }
}
