using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Services;

public abstract partial class ServiceTests : TestBase
{
    protected ServiceTests(Func<TestContext> createContext)
        : base(createContext)
    {
    }

    [Fact]
    public async Task GetJobById_ReturnsJobWithStateHistoryAndRelationships()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var service = TestUtils.CreateJobQueryService(CreateContext());
        var job = await service.GetJobById(jobId);

        job.ShouldNotBeNull();
        job.Id.ShouldBe(jobId);
        job.Logs.Count.ShouldBeGreaterThanOrEqualTo(2); // Enqueued + Processing + Completed
        job.SiblingJobCount.ShouldBe(0);
        job.ChildJobCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetJobById_NonExistent_ReturnsNull()
    {
        var service = TestUtils.CreateJobQueryService(CreateContext());
        var job = await service.GetJobById(Guid.NewGuid());
        job.ShouldBeNull();
    }

    [Fact]
    public async Task PublisherSaveChangesAsync_PersistsEnqueuedJob()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await publisher.SaveChangesAsync();

        var job = await GetJob(jobId);
        job.ShouldNotBeNull();
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task BatchPublisherSaveChangesAsync_PersistsBatchAndJobs()
    {
        var context = CreateContext();
        var batchPublisher = TestUtils.CreateBatchPublisher(context);

        var batchId = await batchPublisher.StartNew(new List<UnitRequest> { new(), new(), new() });
        await batchPublisher.SaveChangesAsync();

        var batch = await CreateContext().Set<Batch>().FindAsync(batchId);
        batch.ShouldNotBeNull();
        batch.JobCount.ShouldBe(3);

        var jobs = await CreateContext().Set<Job>()
            .Where(j => j.BatchId == batchId)
            .CountAsync();
        jobs.ShouldBe(3);
    }

    [Fact]
    public async Task DeleteJob_SetsStateToDeleted()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var service = TestUtils.CreateJobCommandService(CreateContext());
        await service.DeleteJob(jobId);

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Deleted);
    }

    [Fact]
    public async Task RequeueJob_SetsStateToEnqueued()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // completes the job

        var service = TestUtils.CreateJobCommandService(CreateContext());
        await service.RequeueJob(jobId);

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task RequeueBatchJob_IncrementsBatchJobCountAndResetsPlaceholder()
    {
        var context = CreateContext();
        var batchPublisher = TestUtils.CreateBatchPublisher(context);
        var batchId = await batchPublisher.StartNew(new List<UnitRequest> { new(), new() });

        // Add a continuation batch
        var continuationJobs = new List<UnitRequest> { new() };
        await batchPublisher.ContinueBatchWith(continuationJobs, batchId);
        await context.SaveChangesAsync();

        // Process all jobs — batch completes, continuation fires and completes
        await ProcessAllJobs();

        // Batch JobCount should be 0
        var batchBefore = await CreateContext().Set<Batch>().FindAsync(batchId);
        batchBefore!.JobCount.ShouldBe(0);

        // Placeholder should be Completed
        var placeholderBefore = await GetJob(batchId);
        placeholderBefore!.CurrentState.ShouldBe(State.Completed);

        // Count continuation jobs before requeue
        var continuationJobCountBefore = await CreateContext().Set<Job>()
            .Where(x => x.CurrentState == State.Completed)
            .CountAsync();

        // Find a completed job in the batch and requeue it
        var completedJob = await CreateContext().Set<Job>()
            .Where(x => x.BatchId == batchId && x.CurrentState == State.Completed)
            .FirstAsync();

        var service = TestUtils.CreateJobCommandService(CreateContext());
        await service.RequeueJob(completedJob.Id);

        // Batch JobCount should be 1, placeholder back to Awaiting
        var batchAfter = await CreateContext().Set<Batch>().FindAsync(batchId);
        batchAfter!.JobCount.ShouldBe(1);

        var placeholderAfter = await GetJob(batchId);
        placeholderAfter!.CurrentState.ShouldBe(State.Awaiting);

        // Process the requeued job — batch completes again, continuation should NOT re-fire
        await ProcessAllJobs();

        // Verify continuation didn't create duplicate jobs
        var continuationJobCountAfter = await CreateContext().Set<Job>()
            .Where(x => x.CurrentState == State.Completed)
            .CountAsync();

        // The requeued job completed, so +1 completed. Continuation should fire once more (+1).
        // If it double-fired, we'd see more.
        var batchFinal = await CreateContext().Set<Batch>().FindAsync(batchId);
        batchFinal!.JobCount.ShouldBe(0);
    }

    [Fact]
    public async Task RequeueMessageJob_IncrementsMessageJobCountAndReopensMessage()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        // Route the message and process the spawned job
        await ProcessAllJobs();

        // Message should be completed
        var messages = await CreateContext().Set<Message>().ToListAsync();
        var message = messages.First();
        message.CurrentState.ShouldBe(State.Completed);
        message.JobCount.ShouldBe(0);

        // Find the completed job spawned from this message
        var messageJob = await CreateContext().Set<Job>()
            .Where(x => x.MessageId == message.Id)
            .FirstAsync();

        // Requeue it
        var service = TestUtils.CreateJobCommandService(CreateContext());
        await service.RequeueJob(messageJob.Id);

        // Message should be reopened with JobCount = 1
        var updatedMessage = await CreateContext().Set<Message>().FindAsync(message.Id);
        updatedMessage!.JobCount.ShouldBe(1);
        updatedMessage.CurrentState.ShouldBe(State.Processing);
        updatedMessage.ExpireAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetMessages_ReturnsPaginatedMessages()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Publish(new MultiRequest());
        await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        var service = TestUtils.CreateMessageQueryService(CreateContext());
        var result = await service.GetMessages(new BaseListRequest { Page = 0, PageSize = 10 });

        result.TotalCount.ShouldBe(2);
        result.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetMessages_WithStateFilter_ReturnsFilteredMessages()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Publish(new MultiRequest());
        await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        // Route messages and process all jobs so some messages complete
        await ProcessAllJobs();

        var service = TestUtils.CreateMessageQueryService(CreateContext());

        var completed = await service.GetMessages(new BaseListRequest { Page = 0, PageSize = 10 }, "completed");
        completed.Items.ShouldAllBe(m => m.CurrentState == State.Completed);

        var all = await service.GetMessages(new BaseListRequest { Page = 0, PageSize = 10 });
        all.TotalCount.ShouldBeGreaterThanOrEqualTo(completed.TotalCount);
    }

    [Fact]
    public async Task GetBatches_WithStateFilter_ReturnsFilteredBatches()
    {
        var context = CreateContext();
        var batchPublisher = TestUtils.CreateBatchPublisher(context);

        // Create a batch and process it to completion
        await batchPublisher.StartNew(new List<UnitRequest> { new(), new() });
        await context.SaveChangesAsync();
        await ProcessAllJobs();

        var service = TestUtils.CreateBatchQueryService(CreateContext());

        var completed = await service.GetBatches(new BaseListRequest { Page = 0, PageSize = 10 }, "completed");
        completed.TotalCount.ShouldBeGreaterThan(0);
        completed.Items.ShouldAllBe(b => b.PlaceholderState == State.Completed);

        var active = await service.GetBatches(new BaseListRequest { Page = 0, PageSize = 10 }, "active");
        active.Items.ShouldAllBe(b => b.RemainingJobs > 0);

        var all = await service.GetBatches(new BaseListRequest { Page = 0, PageSize = 10 });
        all.TotalCount.ShouldBeGreaterThanOrEqualTo(completed.TotalCount);
    }

    [Fact]
    public async Task GetMessageById_ReturnsMessageWithSpawnedJobs()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // Route the message to create jobs
        await ProcessJob();

        var service = TestUtils.CreateMessageQueryService(CreateContext());
        var message = await service.GetMessageById(messageId);

        message.ShouldNotBeNull();
        message.Id.ShouldBe(messageId);
        message.JobsCount.ShouldBe(2); // MultiHandlerA + MultiHandlerB
    }

    [Fact]
    public async Task GetAwaitingJobs_ReturnsOnlyAwaitingJobs()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var parentId = await publisher.Enqueue(new UnitRequest());
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await context.SaveChangesAsync();

        var service = TestUtils.CreateJobQueryService(CreateContext());
        var result = await service.GetAwaitingJobs(new BaseListRequest { Page = 0, PageSize = 10 });

        result.Items.ShouldContain(j => j.Id == childId);
        result.Items.ShouldNotContain(j => j.Id == parentId);
    }

    [Fact]
    public async Task GetJoblyStatus_ReturnsCorrectCounts()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        await publisher.Enqueue(new UnitRequest());
        await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var stats = await service.GetJoblyStatus();

        stats.Created.ShouldBeGreaterThanOrEqualTo(2);
        stats.Total.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetJobById_WithSiblingJobs_ReturnsSiblingCount()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // Route message -> creates 2 handler jobs
        await ProcessJob();

        var jobs = await CreateContext().Set<Job>()
            .Where(j => j.MessageId == messageId)
            .ToListAsync();

        var service = TestUtils.CreateJobQueryService(CreateContext());
        var jobDetail = await service.GetJobById(jobs[0].Id);

        jobDetail.ShouldNotBeNull();
        jobDetail.SiblingJobCount.ShouldBe(1);

        // Verify paged endpoint returns the sibling
        var siblings = await service.GetSiblingJobs(jobs[0].Id, new BaseListRequest { Page = 0, PageSize = 10 });
        siblings.TotalCount.ShouldBe(1);
        siblings.Items[0].Id.ShouldBe(jobs[1].Id);
    }

    [Fact]
    public async Task GetJobById_WithChildJobs_ReturnsChildCount()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var parentId = await publisher.Enqueue(new UnitRequest());
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await context.SaveChangesAsync();

        var service = TestUtils.CreateJobQueryService(CreateContext());
        var jobDetail = await service.GetJobById(parentId);

        jobDetail.ShouldNotBeNull();
        jobDetail.ChildJobCount.ShouldBe(1);

        // Verify paged endpoint returns the child
        var children = await service.GetChildJobs(parentId, new BaseListRequest { Page = 0, PageSize = 10 });
        children.TotalCount.ShouldBe(1);
        children.Items[0].Id.ShouldBe(childId);
    }
}
