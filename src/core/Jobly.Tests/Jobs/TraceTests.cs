using Jobly.Core;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenRootJob_WhenEnqueued_ThenTraceIdEqualsJobId()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var job = await GetJob(jobId);
        job.TraceId.ShouldBe(jobId);
    }

    [Fact]
    public async Task GivenJobThatSpawnsChild_WhenProcessed_ThenChildInheritsTraceId()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var parentJobId = await publisher.Enqueue(new SpawnChildJobRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // Executes SpawnChildJobHandler which enqueues UnitRequest

        var parentJob = await GetJob(parentJobId);
        parentJob.TraceId.ShouldBe(parentJobId); // Root of the trace

        // Find the spawned child job
        var childJob = await CreateContext().Set<Job>()
            .Where(x => x.SpawnedByJobId == parentJobId)
            .FirstOrDefaultAsync();

        childJob.ShouldNotBeNull();
        childJob.TraceId.ShouldBe(parentJobId); // Same trace as parent
        childJob.SpawnedByJobId.ShouldBe(parentJobId);
    }

    [Fact]
    public async Task GivenJobThatSpawnsChild_WhenFullyProcessed_ThenChildCompletes()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var parentJobId = await publisher.Enqueue(new SpawnChildJobRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs(); // Processes parent (spawns child) then child

        var parentJob = await GetJob(parentJobId);
        parentJob.CurrentState.ShouldBe(State.Completed);

        var childJob = await CreateContext().Set<Job>()
            .Where(x => x.SpawnedByJobId == parentJobId)
            .FirstOrDefaultAsync();

        childJob.ShouldNotBeNull();
        childJob.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task GivenThreeLevelChain_WhenFullyProcessed_ThenAllShareSameTraceId()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        // Level 1: SpawnGrandchildJobRequest → spawns SpawnChildJobRequest → spawns UnitRequest
        var rootJobId = await publisher.Enqueue(new SpawnGrandchildJobRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var rootJob = await GetJob(rootJobId);
        rootJob.TraceId.ShouldBe(rootJobId);

        // Level 2: spawned by root
        var midJob = await CreateContext().Set<Job>()
            .Where(x => x.SpawnedByJobId == rootJobId)
            .FirstOrDefaultAsync();
        midJob.ShouldNotBeNull();
        midJob.TraceId.ShouldBe(rootJobId);

        // Level 3: spawned by mid
        var leafJob = await CreateContext().Set<Job>()
            .Where(x => x.SpawnedByJobId == midJob.Id)
            .FirstOrDefaultAsync();
        leafJob.ShouldNotBeNull();
        leafJob.TraceId.ShouldBe(rootJobId); // Same trace all the way down
        leafJob.SpawnedByJobId.ShouldBe(midJob.Id);
    }

    [Fact]
    public async Task GivenMessageWithMultipleHandlers_WhenRouted_ThenAllJobsShareTraceId()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // Routes message → creates 2 jobs + executes first

        var jobs = await GetJobsForMessage(messageId);
        jobs.Count.ShouldBe(2);

        // Both should share the same TraceId
        jobs[0].TraceId.ShouldNotBeNull();
        jobs[1].TraceId.ShouldNotBeNull();
        jobs[0].TraceId.ShouldBe(jobs[1].TraceId);
    }

    [Fact]
    public async Task GivenTwoSeparateMessages_WhenRouted_ThenJobsHaveDifferentTraceIds()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId1 = await publisher.Publish(new SingleHandlerMessage());
        var messageId2 = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await ProcessAllJobs(); // Routes both messages and processes all jobs

        var jobs1 = await GetJobsForMessage(messageId1);
        var jobs2 = await GetJobsForMessage(messageId2);

        jobs1.Count.ShouldBe(1);
        jobs2.Count.ShouldBe(1);

        jobs1[0].TraceId.ShouldNotBeNull();
        jobs2[0].TraceId.ShouldNotBeNull();
        jobs1[0].TraceId.ShouldNotBe(jobs2[0].TraceId);
    }

    [Fact]
    public async Task GivenJobWithSpawnedChild_WhenGetJobById_ThenTraceJobsIncludesChild()
    {
        await EnsureServerRegistered();
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var parentJobId = await publisher.Enqueue(new SpawnChildJobRequest());
        await context.SaveChangesAsync();

        await ProcessAllJobs();

        var service = TestUtils.CreateJobQueryService(CreateContext());
        var detail = await service.GetJobById(parentJobId);

        detail.ShouldNotBeNull();
        detail.TraceId.ShouldBe(parentJobId);
        detail.TraceJobs.Count.ShouldBe(1);
        detail.TraceJobs[0].Id.ShouldNotBe(parentJobId); // The child, not self
    }
}
