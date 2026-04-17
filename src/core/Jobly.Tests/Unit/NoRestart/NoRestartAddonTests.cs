using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Handlers;
using Jobly.Core.NoRestart;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit.NoRestart;

public abstract class NoRestartAddonTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected NoRestartAddonTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ServiceProvider BuildProvider(bool registerAddon)
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(NoRestartAddonTestsBase).Assembly);
        services.AddLogging();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());
        services.AddScoped<JobContext>();
        services.AddScoped<IJobContext>(x => x.GetRequiredService<JobContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<JoblyConfiguration>>(new OptionsWrapper<JoblyConfiguration>(new JoblyConfiguration()));

        if (registerAddon)
        {
            services.AddJoblyNoRestart();
        }

        return services.BuildServiceProvider();
    }

    [TimedFact]
    public async Task NoRestartAttribute_AddonRegistered_SetsCanBeRestartedFalse()
    {
        var provider = BuildProvider(registerAddon: true);
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
        var publisher = new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, scope.ServiceProvider);

        var jobId = await publisher.Enqueue(new NoRestartAttributeRequest());
        await publisher.SaveChangesAsync();

        var job = await _fixture.CreateContext().Set<Job>().FirstAsync(x => x.Id == jobId);
        job.Metadata.ShouldNotBeNull();
        var metadata = MetadataSerializer.Deserialize(job.Metadata);
        metadata.ShouldContainKey(nameof(ICanBeRestartedMetadata.CanBeRestarted));
        metadata[nameof(ICanBeRestartedMetadata.CanBeRestarted)].ShouldBe(false);
    }

    [TimedFact]
    public async Task RestartAttribute_AddonRegistered_SetsCanBeRestartedTrue()
    {
        var provider = BuildProvider(registerAddon: true);
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
        var publisher = new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, scope.ServiceProvider);

        var jobId = await publisher.Enqueue(new RestartAttributeRequest());
        await publisher.SaveChangesAsync();

        var job = await _fixture.CreateContext().Set<Job>().FirstAsync(x => x.Id == jobId);
        job.Metadata.ShouldNotBeNull();
        var metadata = MetadataSerializer.Deserialize(job.Metadata);
        metadata[nameof(ICanBeRestartedMetadata.CanBeRestarted)].ShouldBe(true);
    }

    [TimedFact]
    public async Task NoRestartAttribute_AddonNotRegistered_AttributeIgnored()
    {
        // Proves the feature is truly opt-in: without AddJoblyNoRestart(), the publish pipeline
        // does not inspect the attribute and no metadata is written.
        var provider = BuildProvider(registerAddon: false);
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
        var publisher = new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, scope.ServiceProvider);

        var jobId = await publisher.Enqueue(new NoRestartAttributeRequest());
        await publisher.SaveChangesAsync();

        var job = await _fixture.CreateContext().Set<Job>().FirstAsync(x => x.Id == jobId);
        job.Metadata.ShouldBeNull();
    }

    [TimedFact]
    public async Task WithRestart_AddonNotRegistered_ExplicitMetadataStillPersisted()
    {
        // .WithRestart() writes directly to JobParameters.Metadata, so it bypasses the
        // publish behavior entirely — the value must persist even without the addon.
        var provider = BuildProvider(registerAddon: false);
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
        var publisher = new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, scope.ServiceProvider);

        var jobId = await publisher.Enqueue(new UnitRequest(), new Jobly.Core.Helper.JobParameters().WithRestart(canBeRestarted: false));
        await publisher.SaveChangesAsync();

        var job = await _fixture.CreateContext().Set<Job>().FirstAsync(x => x.Id == jobId);
        job.Metadata.ShouldNotBeNull();
        var metadata = MetadataSerializer.Deserialize(job.Metadata);
        metadata[nameof(ICanBeRestartedMetadata.CanBeRestarted)].ShouldBe(false);
    }
}

[Collection<PostgreSqlCollection>]
public class NoRestartAddonTests_PostgreSql : NoRestartAddonTestsBase
{
    public NoRestartAddonTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class NoRestartAddonTests_SqlServer : NoRestartAddonTestsBase
{
    public NoRestartAddonTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
