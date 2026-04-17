using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.NoRestart;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration.NoRestart;

public abstract class NoRestartIntegrationTestsBase : IntegrationTestBase
{
    protected NoRestartIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact(60_000)]
    public async Task GivenStaleNoRestartJob_WhenRecovered_ThenMarkedFailed()
    {
        var metadata = MetadataSerializer.Serialize(
            new Dictionary<string, object> { [nameof(ICanBeRestartedMetadata.CanBeRestarted)] = false })!;
        var staleKeepAlive = DateTime.UtcNow.AddMinutes(-10);

        var ctx = Server.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = staleKeepAlive,
            Metadata = metadata,
        });
        await ctx.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Failed, timeout: TimeSpan.FromSeconds(45));

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Failed);
        job.ExpireAt.ShouldBeNull();

        var logs = await Server.GetJobLogs(jobId);
        logs.ShouldContain(l => l.EventType == "Failed" && l.Message.Contains("opted out of restart"));
    }

    [TimedFact(60_000)]
    public async Task GivenNoRestartAttributeJob_WhenPublishedAndStale_ThenMarkedFailed()
    {
        // Publish through the real pipeline so NoRestartPublishBehavior writes CanBeRestarted=false.
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new NoRestartAttributeRequest());
        await publisher.SaveChangesAsync();

        // Let the worker run it to completion so we know the behavior wrote metadata that persisted.
        await Server.WaitForJobState(jobId, State.Completed);
        var published = await Server.GetJob(jobId);
        published.Metadata.ShouldNotBeNull();
        var publishedMeta = MetadataSerializer.Deserialize(published.Metadata);
        publishedMeta[nameof(ICanBeRestartedMetadata.CanBeRestarted)].ShouldBe(false);

        // Force the completed job back into Processing with a stale keep-alive — simulates a
        // crashed worker. The stale recovery task must now honor the metadata and mark it Failed.
        var ctx = Server.CreateContext();
        await ctx.Set<Job>()
            .Where(x => x.Id == jobId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.CurrentState, State.Processing)
                .SetProperty(p => p.LastKeepAlive, DateTime.UtcNow.AddMinutes(-10)));

        await Server.WaitForJobState(jobId, State.Failed, timeout: TimeSpan.FromSeconds(45));

        var job = await Server.GetJob(jobId);
        job.CurrentState.ShouldBe(State.Failed);
        job.ExpireAt.ShouldBeNull();
    }
}

[Collection<PostgreSqlIntegrationCollection>]
public class NoRestartIntegrationTests_PostgreSql : NoRestartIntegrationTestsBase
{
    public NoRestartIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class NoRestartIntegrationTests_SqlServer : NoRestartIntegrationTestsBase
{
    public NoRestartIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
