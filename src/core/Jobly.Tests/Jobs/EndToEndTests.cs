using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Jobly.Tests.Jobs;

/// <summary>
/// Full end-to-end test: boots a real IHost with workers, seeds a complex workload,
/// waits for completion, then asserts on the final database state.
/// </summary>
[Collection("PostgreSql")]
public class EndToEndTests : IAsyncLifetime
{
    private static readonly string[] DefaultQueues = ["default"];
    private readonly PostgreSqlFixture _fixture;

    public EndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenComplexWorkload_WhenProcessedByRealWorkers_ThenAllJobsReachTerminalState()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped<TestContext>(_ => _fixture.CreateContext());
                services.AddJobHandlers(typeof(UnitRequest).Assembly);
                services.AddPipelineBehaviors(typeof(UnitRequest).Assembly);
                services.AddSingleton<CounterService>();
                services.AddSingleton<MultiHandlerCounter>();
                services.AddJoblyWorker<TestContext>(options =>
                {
                    options.WorkerCount = 5;
                    options.ServerName = "e2e-test-server";
                    options.Queues = DefaultQueues;
                    options.PollingInterval = TimeSpan.FromMilliseconds(100);
                    options.HealthCheckInterval = TimeSpan.FromSeconds(5);
                });
            })
            .Build();

        await host.StartAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            var batchPublisher = scope.ServiceProvider.GetRequiredService<IBatchPublisher>();
            var context = scope.ServiceProvider.GetRequiredService<TestContext>();

            // 1. Simple jobs (50)
            for (var i = 0; i < 50; i++)
            {
                await publisher.Enqueue(new UnitRequest());
            }

            // 2. Jobs that spawn children (10 → 10 children = 20 total)
            for (var i = 0; i < 10; i++)
            {
                await publisher.Enqueue(new SpawnChildJobRequest());
            }

            // 3. Three-level trace chain (5 → 5 mid → 5 leaf = 15 total)
            for (var i = 0; i < 5; i++)
            {
                await publisher.Enqueue(new SpawnGrandchildJobRequest());
            }

            // 4. Jobs that spawn batches with continuations (3 parents + 3 batches of 3 + 3 continuations + placeholders)
            for (var i = 0; i < 3; i++)
            {
                await publisher.Enqueue(new SpawnBatchRequest());
            }

            // 5. Messages with multiple handlers (5 messages → 10 jobs)
            for (var i = 0; i < 5; i++)
            {
                await publisher.Publish(new MultiRequest());
            }

            // 6. Failing jobs (10, no retries)
            for (var i = 0; i < 10; i++)
            {
                await publisher.Enqueue(new ThrowExceptionRequest());
            }

            // 7. Failing jobs with retries (5, maxRetries=2 → will retry twice then fail)
            for (var i = 0; i < 5; i++)
            {
                await publisher.Enqueue(new ThrowExceptionRequest(), maxRetries: 2);
            }

            // 8. Batch of 10 jobs → continuation of 3 (directly, not via handler)
            var batchJobs = Enumerable.Range(0, 10).Select(_ => new UnitRequest()).ToList();
            var batchId = await batchPublisher.StartNew(batchJobs);
            var continuationJobs = Enumerable.Range(0, 3).Select(_ => new UnitRequest()).ToList();
            await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

            // 9. Continuations: parent → child chain (5 chains of 2 = 10)
            for (var i = 0; i < 5; i++)
            {
                var parentId = await publisher.Enqueue(new UnitRequest());
                await publisher.Enqueue(new UnitRequest(), parentId);
            }

            await context.SaveChangesAsync();
        }

        var maxWait = TimeSpan.FromSeconds(60);
        var start = DateTime.UtcNow;
        var allDone = false;

        while (DateTime.UtcNow - start < maxWait)
        {
            await Task.Delay(500);

            await using var checkScope = host.Services.CreateAsyncScope();
            var checkContext = checkScope.ServiceProvider.GetRequiredService<TestContext>();

            var activeJobs = await checkContext.Set<Job>()
                .CountAsync(j =>
                    j.CurrentState == State.Enqueued ||
                    j.CurrentState == State.Processing ||
                    j.CurrentState == State.Awaiting);

            var activeMessages = await checkContext.Set<Message>()
                .CountAsync(m => m.CurrentState != State.Completed);

            if (activeJobs == 0 && activeMessages == 0)
            {
                // Wait a bit more for in-flight stat writes to complete
                await Task.Delay(2000);
                allDone = true;
                break;
            }
        }

        // Stop the host gracefully
        await host.StopAsync();

        allDone.ShouldBeTrue("Timed out waiting for all jobs to complete");

        var ctx = _fixture.CreateContext();

        // Helper: exclude batch placeholder jobs (same as dashboard)
        var batchIds = ctx.Set<Batch>().Select(b => b.Id);
        var jobs = ctx.Set<Job>().Where(j => !batchIds.Contains(j.Id));

        // No jobs stuck in non-terminal states
        var stuckJobs = await jobs
            .Where(j => j.CurrentState == State.Enqueued ||
                        j.CurrentState == State.Processing ||
                        j.CurrentState == State.Awaiting)
            .CountAsync();
        stuckJobs.ShouldBe(0, "No jobs should be stuck in non-terminal states");

        // All messages completed
        var incompleteMessages = await ctx.Set<Message>()
            .Where(m => m.CurrentState != State.Completed)
            .CountAsync();
        incompleteMessages.ShouldBe(0, "All messages should be completed");

        // Completed job count
        var completedJobs = await jobs
            .CountAsync(j => j.CurrentState == State.Completed);
        completedJobs.ShouldBeGreaterThan(100, "Should have many completed jobs");

        // Failed job count (10 no-retry + 5 with-retry = 15 failed)
        var failedJobs = await jobs
            .CountAsync(j => j.CurrentState == State.Failed);
        failedJobs.ShouldBe(15, "Should have exactly 15 failed jobs");

        // All batches completed (counter = 0)
        var incompleteBatches = await ctx.Set<Batch>()
            .Where(b => b.Counter > 0)
            .CountAsync();
        incompleteBatches.ShouldBe(0, "All batch counters should be 0");

        // All jobs should have a TraceId
        var jobsWithoutTrace = await jobs
            .CountAsync(j => j.TraceId == null);
        jobsWithoutTrace.ShouldBe(0, "All jobs should have a TraceId");

        // Spawned jobs should have SpawnedByJobId set
        var spawnedJobs = await jobs
            .Where(j => j.SpawnedByJobId != null)
            .CountAsync();
        spawnedJobs.ShouldBeGreaterThan(0, "Should have spawned jobs with trace links");

        // No workers still holding jobs
        var jobsWithWorker = await jobs
            .CountAsync(j => j.CurrentWorkerId != null);
        jobsWithWorker.ShouldBe(0, "No terminal jobs should have a CurrentWorkerId");

        // LastKeepAlive should be null on all terminal jobs
        var jobsWithKeepAlive = await jobs
            .CountAsync(j => j.LastKeepAlive != null);
        jobsWithKeepAlive.ShouldBe(0, "No terminal jobs should have a LastKeepAlive");

        // Statistics should be consistent
        // completedJobs/failedJobs exclude batch placeholders (hidden from dashboard)
        await TestUtils.AggregateCounters(_fixture.CreateContext());

        var statsSucceeded = await ctx.Set<Statistic>()
            .Where(x => x.Key == "stats:succeeded").Select(x => x.Value).FirstOrDefaultAsync();
        var statsFailed = await ctx.Set<Statistic>()
            .Where(x => x.Key == "stats:failed").Select(x => x.Value).FirstOrDefaultAsync();

        statsSucceeded.ShouldBe(completedJobs, "stats:succeeded should match completed job count");
        statsFailed.ShouldBe(failedJobs, "stats:failed should match failed job count");
    }
}
