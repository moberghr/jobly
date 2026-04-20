using Jobly.Core.Data.Queries;
using Jobly.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker;

/// <summary>
/// Hosted service that constructs and manages the lifecycle of per-worker
/// <see cref="JoblyWorkerService{TContext}"/> + <see cref="JoblyWorker{TContext}"/> pairs when
/// <see cref="JoblyWorkerConfiguration.UseDispatcher"/> is false. Depends on
/// <see cref="ServerRegistrationState"/> having been populated by
/// <see cref="JoblyServerRegistration{TContext}"/>, which is registered first. No-ops when
/// dispatcher mode is enabled.
/// </summary>
public class JoblySingleWorkerHost<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly JoblyWorkerConfiguration _configuration;
    private readonly IOptions<JoblyWorkerConfiguration> _configurationOptions;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly PauseStateHolder _pauseStateHolder;
    private readonly IJoblyNotificationTransport _notificationTransport;
    private readonly IJoblySqlQueries<TContext> _sqlQueries;
    private readonly ServerRegistrationState _state;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<BackgroundService> _workers = [];

    public JoblySingleWorkerHost(
        IOptions<JoblyWorkerConfiguration> configuration,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider,
        PauseStateHolder pauseStateHolder,
        IJoblyNotificationTransport notificationTransport,
        IJoblySqlQueries<TContext> sqlQueries,
        ServerRegistrationState state,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration.Value;
        _configurationOptions = configuration;
        _serviceScopeFactory = serviceScopeFactory;
        _timeProvider = timeProvider;
        _pauseStateHolder = pauseStateHolder;
        _notificationTransport = notificationTransport;
        _sqlQueries = sqlQueries;
        _state = state;
        _loggerFactory = loggerFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_configuration.UseDispatcher)
        {
            return;
        }

        foreach (var registration in _state.Groups)
        {
            foreach (var workerId in registration.WorkerIds)
            {
                var workerService = new JoblyWorkerService<TContext>(
                    workerId,
                    _serviceScopeFactory,
                    _loggerFactory.CreateLogger<JoblyWorkerService<TContext>>(),
                    _configurationOptions,
                    registration.Config,
                    _timeProvider,
                    _sqlQueries,
                    _notificationTransport);

                var worker = new JoblyWorker<TContext>(
                    workerService,
                    _loggerFactory.CreateLogger<JoblyWorker<TContext>>(),
                    registration.Config,
                    _pauseStateHolder,
                    registration.GroupEntityId);

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
