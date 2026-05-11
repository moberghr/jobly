using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.Concurrency;
using Warp.Core.Enums;
using Warp.Core.Helper;
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

    [TimedFact]
    public async Task GivenSemaphoreLimit2_WhenThreeJobsEnqueued_ThenTwoRunConcurrentlyAndThirdWaits()
    {
        // Wait-mode semaphore with limit=2: exactly 2 of 3 enqueued jobs may be in the
        // handler simultaneously; the third must bounce off the semaphore (Requeued) until a
        // slot frees. BarrierSignal pins handler entry deterministically, so the cap is
        // proven directly rather than inferred from observed peak in-flight under jitter.
        var barrier = new BarrierSignal();

        await using var server = await WarpTestServer.StartAsync(
            Fixture,
            cfg => cfg.Services.AddSingleton(barrier));
        var publisher = server.CreatePublisher();

        var job1Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithSemaphore("test-semaphore-2", 2, ConcurrencyMode.Wait));
        var job2Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithSemaphore("test-semaphore-2", 2, ConcurrencyMode.Wait));
        var job3Id = await publisher.Enqueue(new BarrierRequest(), new JobParameters().WithSemaphore("test-semaphore-2", 2, ConcurrencyMode.Wait));
        await publisher.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Two jobs enter the handler — both slots filled.
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // The third must not enter while both slots are held; 500 ms covers ~5 polling cycles.
        var spuriousEntry = await barrier.Running.WaitAsync(TimeSpan.FromMilliseconds(500), Xunit.TestContext.Current.CancellationToken);
        spuriousEntry.ShouldBeFalse("limit=2 must hold; the third job cannot enter while two slots are held");

        // Release one slot — the third should now enter.
        barrier.CanFinish.Release();
        await barrier.Running.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // Release the remaining two; all complete.
        barrier.CanFinish.Release(2);
        await server.WaitForCompletion();

        foreach (var id in new[] { job1Id, job2Id, job3Id })
        {
            var job = await server.GetJob(id);
            job.CurrentState.ShouldBe(State.Completed);
        }
    }
}
