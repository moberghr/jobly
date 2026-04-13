using System.Text.Json;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class MetadataIntegrationTestsBase : IntegrationTestBase
{
    protected MetadataIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PublishJob_WithPublishPipeline_MetadataPersistedAndAvailableInHandler()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new MetadataRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForJobState(jobId, State.Completed);

        // Verify metadata was persisted to DB
        var job = await Server.GetJob(jobId);
        job.Metadata.ShouldNotBeNull();
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(job.Metadata)!;
        metadata["test-key"].ShouldBe("test-value");
        metadata["source"].ShouldBe("publish-pipeline");

        // Verify handler received it via IJobContext
        var capture = Server.GetService<MetadataCapture>();
        capture.CapturedMetadata.ShouldNotBeNull();
        capture.CapturedMetadata["test-key"].ShouldBe("test-value");
        capture.CapturedMetadata["source"].ShouldBe("publish-pipeline");
    }

    [Fact]
    public async Task PublishMessage_MetadataInheritedByRoutedChildJobs()
    {
        var publisher = Server.CreatePublisher();
        var messageId = await publisher.Publish(new MultiRequest());
        await publisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        // All child jobs should inherit the message's metadata
        var ctx = Server.CreateContext();
        var childJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync();

        childJobs.Count.ShouldBeGreaterThan(0);
        foreach (var child in childJobs)
        {
            child.Metadata.ShouldNotBeNull();
            var childMetadata = JsonSerializer.Deserialize<Dictionary<string, string>>(child.Metadata)!;
            childMetadata["test-key"].ShouldBe("test-value");
        }
    }

    [Fact]
    public async Task BatchPublish_AllChildrenGetSameMetadata()
    {
        var batchPublisher = Server.CreateBatchPublisher();
        var jobs = Enumerable.Range(0, 3).Select(_ => new UnitRequest()).ToList();
        var batchId = await batchPublisher.StartNew(jobs);
        await batchPublisher.SaveChangesAsync();

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();
        var childJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job)
            .ToListAsync();

        childJobs.Count.ShouldBe(3);
        foreach (var child in childJobs)
        {
            child.Metadata.ShouldNotBeNull();
            var childMetadata = JsonSerializer.Deserialize<Dictionary<string, string>>(child.Metadata)!;
            childMetadata["test-key"].ShouldBe("test-value");
        }
    }
}

[Collection("PostgreSql-Integration")]
public class MetadataIntegrationTests_PostgreSql : MetadataIntegrationTestsBase
{
    public MetadataIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection("SqlServer-Integration")]
[Trait("Category", "SqlServer")]
public class MetadataIntegrationTests_SqlServer : MetadataIntegrationTestsBase
{
    public MetadataIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
