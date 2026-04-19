using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Core;

[GenerateDatabaseTests(FixtureKind.Integration)]
public abstract class MetadataIntegrationTestsBase : IntegrationTestBase
{
    protected MetadataIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact]
    public async Task PublishJob_WithPublishPipeline_MetadataPersistedAndAvailableInHandler()
    {
        var publisher = Server.CreatePublisher();
        var jobId = await publisher.Enqueue(new MetadataRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForJobState(jobId, State.Completed);

        // Verify metadata was persisted to DB
        var job = await Server.GetJob(jobId);
        job.Metadata.ShouldNotBeNull();
        var metadata = MetadataSerializer.Deserialize(job.Metadata)!;
        metadata["test-key"].ShouldBe("test-value");
        metadata["source"].ShouldBe("publish-pipeline");

        // Verify handler received it via IJobContext
        var capture = Server.GetService<MetadataCapture>();
        capture.CapturedMetadata.ShouldNotBeNull();
        capture.CapturedMetadata["test-key"].ShouldBe("test-value");
        capture.CapturedMetadata["source"].ShouldBe("publish-pipeline");
    }

    [TimedFact]
    public async Task PublishMessage_MetadataInheritedByRoutedChildJobs()
    {
        var publisher = Server.CreatePublisher();
        var messageId = await publisher.Publish(new MultiRequest());
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        // All child jobs should inherit the message's metadata
        var ctx = Server.CreateContext();
        var childJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        childJobs.Count.ShouldBeGreaterThan(0);
        foreach (var child in childJobs)
        {
            child.Metadata.ShouldNotBeNull();
            var childMetadata = MetadataSerializer.Deserialize(child.Metadata)!;
            childMetadata["test-key"].ShouldBe("test-value");
        }
    }

    [TimedFact]
    public async Task BatchPublish_AllChildrenGetSameMetadata()
    {
        var batchPublisher = Server.CreateBatchPublisher();
        var jobs = Enumerable.Range(0, 3).Select(_ => new UnitRequest()).ToList();
        var batchId = await batchPublisher.StartNew(jobs);
        await batchPublisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await Server.WaitForCompletion();

        var ctx = Server.CreateContext();
        var childJobs = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == batchId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        childJobs.Count.ShouldBe(3);
        foreach (var child in childJobs)
        {
            child.Metadata.ShouldNotBeNull();
            var childMetadata = MetadataSerializer.Deserialize(child.Metadata)!;
            childMetadata["test-key"].ShouldBe("test-value");
        }
    }
}
