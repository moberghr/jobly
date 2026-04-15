using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Helper;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class MetadataPropagationIntegrationTestsBase : IntegrationTestBase
{
    protected MetadataPropagationIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task GivenHandlerThatWritesMetadata_WhenCompleted_ThenHandlerMetadataPersisted()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new MetadataWriterRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Completed);

        var ctx = Server.CreateContext();
        var job = await ctx.Set<Job>()
            .Where(x => x.Id == jobId)
            .FirstAsync();

        var metadata = MetadataSerializer.Deserialize(job.Metadata);
        metadata.ShouldContainKey("HandlerWrote");
        metadata["HandlerWrote"].ShouldBe("from-handler");
    }

    [TimedFact]
    public async Task GivenPublishPipelineAndHandler_WhenCompleted_ThenBothMetadataKeysPersisted()
    {
        // RetryPublishBehavior sets MaxRetries at publish time
        // Handler sets HandlerWrote at execution time
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new MetadataWriterRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Completed);

        var ctx = Server.CreateContext();
        var job = await ctx.Set<Job>()
            .Where(x => x.Id == jobId)
            .FirstAsync();

        var metadata = MetadataSerializer.Deserialize(job.Metadata);

        // From RetryPublishBehavior (publish time)
        metadata.ShouldContainKey("MaxRetries");

        // From handler (execution time)
        metadata.ShouldContainKey("HandlerWrote");
        metadata["HandlerWrote"].ShouldBe("from-handler");
    }

    [TimedFact]
    public async Task GivenUserMetadataAtPublishTime_WhenCompleted_ThenUserMetadataPreserved()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new MetadataWriterRequest(), new JobParameters
        {
            Metadata = new Dictionary<string, object> { ["UserKey"] = "user-value" },
        });
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Completed);

        var ctx = Server.CreateContext();
        var job = await ctx.Set<Job>()
            .Where(x => x.Id == jobId)
            .FirstAsync();

        var metadata = MetadataSerializer.Deserialize(job.Metadata);

        // User-set at publish time
        metadata.ShouldContainKey("UserKey");
        metadata["UserKey"].ShouldBe("user-value");

        // Addon-set at publish time (RetryPublishBehavior)
        metadata.ShouldContainKey("MaxRetries");

        // Handler-set at execution time
        metadata.ShouldContainKey("HandlerWrote");
    }

    [TimedFact]
    public async Task GivenFailingJobWithRetry_WhenFailed_ThenRetryMetadataPersisted()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new ThrowExceptionRequest(), new JobParameters
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
        var job = await ctx.Set<Job>()
            .Where(x => x.Id == jobId)
            .FirstAsync();

        // Retry behavior wrote RetriedTimes to metadata during failure handling
        var metadata = MetadataSerializer.Deserialize(job.Metadata);
        metadata.ShouldContainKey("RetriedTimes");
        Convert.ToInt32(metadata["RetriedTimes"]).ShouldBe(1);
    }

    [TimedFact]
    public async Task GivenChildJobSpawnedByHandler_WhenCompleted_ThenChildInheritsParentMetadata()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new SpawnChildJobRequest(), new JobParameters
        {
            Metadata = new Dictionary<string, object> { ["ParentKey"] = "inherited" },
        });
        await publisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();
        var children = await ctx.Set<Job>()
            .Where(x => x.SpawnedByJobId == jobId)
            .Where(x => x.Kind == JobKind.Job)
            .ToListAsync();

        children.Count.ShouldBeGreaterThan(0);

        foreach (var child in children)
        {
            var metadata = MetadataSerializer.Deserialize(child.Metadata);
            metadata.ShouldContainKey("ParentKey");
            metadata["ParentKey"].ShouldBe("inherited");
        }
    }
}

[Collection<PostgreSqlIntegrationCollection>]
public class MetadataPropagationIntegrationTests_PostgreSql : MetadataPropagationIntegrationTestsBase
{
    public MetadataPropagationIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class MetadataPropagationIntegrationTests_SqlServer : MetadataPropagationIntegrationTestsBase
{
    public MetadataPropagationIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
