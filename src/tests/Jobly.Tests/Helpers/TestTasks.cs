using Jobly.Core;
using Jobly.Core.Data;
using Jobly.Core.Data.Queries;
using Jobly.Core.Notifications;
using Jobly.Core.Services;
using Jobly.Provider.PostgreSql;
using Jobly.Provider.SqlServer;
using Jobly.Worker;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobly.Tests.Helpers;

/// <summary>
/// Factory helpers that construct fully-wired background task instances for unit tests.
/// Absorbs the ctor boilerplate (logger, options, lock provider, sql queries) so call sites
/// pass just the context + configurable knobs (timeouts etc.) that actually matter.
/// </summary>
public static class TestTasks
{
    // Throwaway scope factory for tasks whose instance methods don't create scopes
    // (StaleJobRecoveryTask, ServerCleanupTask). MessageRoutingTask needs a real one with
    // registered handlers — pass it via the scopeFactory parameter.
    public static readonly IServiceScopeFactory EmptyScopeFactory =
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    public static readonly IJoblyNotificationTransport NullTransport = new NullNotificationTransport();

    public static IJoblySqlQueries<TContext> QueriesFor<TContext>(TContext context)
        where TContext : DbContext
    {
        var names = JoblyJobTableNames.FromModel(context.Model);
        return IsPostgres(context)
            ? new PostgresJoblySqlQueries<TContext>(names)
            : new SqlServerJoblySqlQueries<TContext>(names);
    }

    public static IDatabaseExceptionClassifier ClassifierFor<TContext>(TContext context)
        where TContext : DbContext
    {
        return IsPostgres(context)
            ? new PostgresExceptionClassifier()
            : new SqlServerExceptionClassifier();
    }

    private static bool IsPostgres<TContext>(TContext context)
        where TContext : DbContext
    {
        return context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static IJoblySqlQueries<TContext> QueriesFromScope<TContext>(IServiceScopeFactory scopeFactory)
        where TContext : DbContext
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        return QueriesFor(context);
    }

    public static JobCommandService<TContext> CreateJobCommandService<TContext>(TContext context)
        where TContext : DbContext
    {
        return new JobCommandService<TContext>(
            context,
            TimeProvider.System,
            Options.Create(new JoblyConfiguration()),
            NullTransport,
            Jobly.Tests.Helpers.TestTasks.QueriesFor(context));
    }

    public static MessageRouter<TContext> CreateMessageRouter<TContext>(
        TContext context,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider)
        where TContext : DbContext
    {
        return new MessageRouter<TContext>(
            context,
            timeProvider,
            scopeFactory,
            Jobly.Tests.Helpers.TestTasks.QueriesFor(context),
            Options.Create(new JoblyWorkerConfiguration()));
    }

    public static StaleJobRecovery<TContext> CreateStaleJobRecovery<TContext>(
        TContext context,
        TimeProvider timeProvider,
        TimeSpan invisibilityTimeout,
        bool restartByDefault = true)
        where TContext : DbContext
    {
        return new StaleJobRecovery<TContext>(
            context,
            timeProvider,
            Jobly.Tests.Helpers.TestTasks.QueriesFor(context),
            Options.Create(new JoblyWorkerConfiguration
            {
                InvisibilityTimeout = invisibilityTimeout,
                RestartStaleJobsByDefault = restartByDefault,
            }));
    }

    public static CounterAggregator<TContext> CreateCounterAggregator<TContext>(TContext context)
        where TContext : DbContext
    {
        return new CounterAggregator<TContext>(
            context,
            Options.Create(new JoblyWorkerConfiguration()));
    }

    public static ScheduledJobActivation<TContext> CreateScheduledJobActivation<TContext>(
        TContext context,
        TimeProvider timeProvider,
        IJoblyNotificationTransport? transport = null)
        where TContext : DbContext
    {
        return new ScheduledJobActivation<TContext>(
            context,
            timeProvider,
            transport ?? NullTransport,
            Options.Create(new JoblyWorkerConfiguration()));
    }

    public static Orchestrator<TContext> CreateOrchestrator<TContext>(
        TContext context,
        TimeProvider timeProvider,
        TimeSpan jobExpirationTimeout)
        where TContext : DbContext
    {
        return new Orchestrator<TContext>(
            context,
            timeProvider,
            Options.Create(new JoblyWorkerConfiguration { JobExpirationTimeout = jobExpirationTimeout }));
    }

    public static RecurringJobScheduler<TContext> CreateRecurringJobScheduler<TContext>(
        TContext context,
        TimeProvider timeProvider)
        where TContext : DbContext
    {
        return new RecurringJobScheduler<TContext>(
            context,
            timeProvider,
            Options.Create(new JoblyWorkerConfiguration()));
    }

    public static ExpirationCleanup<TContext> CreateExpirationCleanup<TContext>(
        TContext context,
        TimeProvider timeProvider,
        int batchSize = 1000)
        where TContext : DbContext
    {
        return new ExpirationCleanup<TContext>(
            context,
            timeProvider,
            Options.Create(new JoblyWorkerConfiguration { ExpirationBatchSize = batchSize }));
    }

    public static ServerCleanup<TContext> CreateServerCleanup<TContext>(
        TContext context,
        TimeProvider timeProvider,
        TimeSpan healthCheckTimeout)
        where TContext : DbContext
    {
        return new ServerCleanup<TContext>(
            context,
            timeProvider,
            Jobly.Tests.Helpers.TestTasks.QueriesFor(context),
            Options.Create(new JoblyWorkerConfiguration { HealthCheckTimeout = healthCheckTimeout }));
    }
}
