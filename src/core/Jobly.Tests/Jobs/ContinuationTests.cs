using Jobly.Core.Enums;
using Jobly.Core;
using Jobly.Tests.TestData.Handlers;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task Publish_Continuations_ChildJobCurrentStateShouldSwitchForAwaitToCompleted()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobRequest = new UnitRequest();
        Guid jobId = await publisher.Enqueue(jobRequest);
        Guid childJobId = await publisher.Enqueue(jobRequest, jobId);
        await context.SaveChangesAsync();
        var childJob = await GetJob(childJobId);

        childJob.ShouldNotBeNull();
        childJob.CurrentState.ShouldBe(State.Awaiting);
        childJob.ParentJobId.ShouldBe(jobId);

        for (int i = 0; i < 3; i++)
        {
            await ProcessJob();
        }

        childJob = await GetJob(childJobId);

        childJob.ShouldNotBeNull();
        childJob.CurrentState.ShouldBe(State.Completed);
    }

    [Fact]
    public async Task Publish_Continuations_ParentJobFailChildJobShouldBeStateAwait()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobRequest = new UnitRequest();

        Guid jobId = await CreateFailedRetryJob(context, 0, 0, null);
        Guid childJobId = await publisher.Enqueue(jobRequest, jobId);
        await context.SaveChangesAsync();

        for (int i = 0; i < 2; i++)
        {
            await ProcessJob();
        }

        var childJob = await GetJob(childJobId);

        childJob.ShouldNotBeNull();
        childJob.CurrentState.ShouldBe(State.Awaiting);
    }
}

