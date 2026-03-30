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
