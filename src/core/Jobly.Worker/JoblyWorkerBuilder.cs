using Jobly.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Worker;

/// <summary>
/// Builder passed to <see cref="ServiceConfiguration.AddJoblyWorker{TContext}"/>. Inherits
/// <see cref="JoblyWorkerConfiguration"/> so config fields are set directly on the builder,
/// and implements <see cref="IJoblyBuilder{TContext}"/> so every Core addon extension
/// (AddMutex, AddRetry, AddCircuitBreaker, AddNoRestart, and future provider extensions like
/// UsePostgreSql) can be called from inside the AddJoblyWorker lambda.
/// </summary>
public sealed class JoblyWorkerBuilder<TContext> : JoblyWorkerConfiguration, IJoblyBuilder<TContext>
    where TContext : DbContext
{
    public JoblyWorkerBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    JoblyConfiguration IJoblyBuilder<TContext>.Configuration => this;
}
