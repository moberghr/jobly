using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.TestData.Handlers;

namespace Warp.Tests.Features.Concurrency;

[GenerateDatabaseTests]
public abstract class SemaphoreIntegrationTestsBase : IntegrationTestBase
{
    protected SemaphoreIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [TimedFact(30_000)]
    public async Task Limit5_AcquiresFiveConcurrentJobs()
    {
        // Enqueue 10 jobs each tagged with [Semaphore("limit-5-key", 5)] (Wait mode default).
        // The handler delays so multiple jobs overlap; the tracker captures the peak in-flight
        // count. With limit=5 the maximum concurrent jobs for the key must be exactly 5.
        var tracker = new ConcurrencyTracker();

        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            configure: null,
            configureServices: services => services.AddSingleton(tracker));

        var publisher = server.CreatePublisher();

        const int jobCount = 10;
        var jobIds = new List<Guid>(jobCount);
        for (var i = 0; i < jobCount; i++)
        {
            var id = await publisher.Enqueue(new SemaphoreLimit5Request());
            jobIds.Add(id);
        }

        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.WaitForCompletion(timeout: TimeSpan.FromSeconds(25));

        foreach (var id in jobIds)
        {
            var job = await server.GetJob(id);
            job.CurrentState.ShouldBe(State.Completed);
        }

        tracker.Completed.ShouldBe(jobCount);

        // Per-key peak in-flight must equal the configured limit (5). It can't exceed 5
        // because the semaphore enforces it; with 10 jobs delayed 150ms each, default worker
        // headroom (min(ProcessorCount * 5, 20)) ensures we observe the cap.
        tracker.MaxObservedFor("limit-5-key").ShouldBeLessThanOrEqualTo(5);
        tracker.MaxObservedFor("limit-5-key").ShouldBeGreaterThan(1, "with 10 jobs and limit=5, parallelism must occur");
    }
}
