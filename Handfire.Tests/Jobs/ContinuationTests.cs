using Handfire.Core.Enums;
using Handfire.Core;
using Handfire.Tests.TestData.Handlers;
using Shouldly;

namespace Handfire.Tests.Jobs;

public abstract partial class HandfireTests : TestBase
{
    [Fact]
    public async Task Publish_Continuations_ChildJobCurrentStateShouldSwitchForAwaitToCompleted()
    {
        var context = CreateContext();
        var publisher = new Publisher<TestContext>(context, 0);
        var jobRequest = new UnitRequest();
        string jobId = await publisher.Publish(jobRequest);
        string childJobId = await publisher.Publish(jobRequest, jobId);
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
        var publisher = new Publisher<TestContext>(context, 0);
        var jobRequest = new UnitRequest();

        string jobId = await CreateFailedRetryJob(context, 0, 0, null);
        string childJobId = await publisher.Publish(jobRequest, jobId);
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

