using System.Text.Json;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class ScopeIsolationIntegrationTestsBase : IntegrationTestBase
{
    protected ScopeIsolationIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenHandlerThatAddsEntityAndThrows_WhenProcessed_ThenEntityNotPersisted()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new AddEntityThenThrowRequest(), new JobParameters
        {
            Metadata = new Dictionary<string, object> { ["MaxRetries"] = 0 },
        });
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Failed);

        var ctx = Server.CreateContext();
        var leaked = await ctx.Set<Counter>()
            .Where(x => x.Key == "handler-leaked-entity")
            .AnyAsync();
        leaked.ShouldBeFalse();
    }

    [TimedFact]
    public async Task GivenHandlerThatAddsEntitySavesAndThrows_WhenProcessed_ThenEntityPersisted()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new AddEntitySaveThenThrowRequest(), new JobParameters
        {
            Metadata = new Dictionary<string, object> { ["MaxRetries"] = 0 },
        });
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Failed);

        var ctx = Server.CreateContext();
        var committed = await ctx.Set<Counter>()
            .Where(x => x.Key == "handler-committed-entity")
            .AnyAsync();
        committed.ShouldBeTrue();
    }

    [TimedFact]
    public async Task GivenHandlerThatAddsEntityAndThrows_WithRetry_ThenEntityNotPersisted()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new AddEntityThenThrowRequest(), new JobParameters
        {
            Metadata = new Dictionary<string, object>
            {
                ["MaxRetries"] = 1,
                ["RetryDelays"] = new[] { 1 },
            },
        });
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Failed, timeout: TimeSpan.FromSeconds(30));

        var ctx = Server.CreateContext();
        var leaked = await ctx.Set<Counter>()
            .Where(x => x.Key == "handler-leaked-entity")
            .AnyAsync();
        leaked.ShouldBeFalse();

        var job = await ctx.Set<Job>()
            .Where(x => x.Id == jobId)
            .FirstAsync();
        job.CurrentState.ShouldBe(State.Failed);
    }

    [TimedFact]
    public async Task GivenSuccessfulHandler_WhenChildJobPublished_ThenChildJobPersisted()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new SpawnChildJobRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();
        var childJobs = await ctx.Set<Job>()
            .Where(x => x.SpawnedByJobId == jobId)
            .Where(x => x.Kind == JobKind.Job)
            .ToListAsync();
        childJobs.Count.ShouldBeGreaterThan(0);
    }

    [TimedFact]
    public async Task GivenSuccessfulHandler_WhenMetadataModifiedByPipeline_ThenMetadataPersisted()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Completed);

        var ctx = Server.CreateContext();
        var job = await ctx.Set<Job>()
            .Where(x => x.Id == jobId)
            .FirstAsync();
        job.CurrentState.ShouldBe(State.Completed);

        // Metadata should be persisted even on success (RetryPublishBehavior injects $maxRetries)
        job.Metadata.ShouldNotBeNull();
        var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(job.Metadata);
        metadata.ShouldNotBeNull();
        metadata.ShouldContainKey("MaxRetries");
    }
}

[Collection<PostgreSqlIntegrationCollection>]
[Trait("Category", "PostgreSql")]
public class ScopeIsolationIntegrationTests_PostgreSql : ScopeIsolationIntegrationTestsBase
{
    public ScopeIsolationIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class ScopeIsolationIntegrationTests_SqlServer : ScopeIsolationIntegrationTestsBase
{
    public ScopeIsolationIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
