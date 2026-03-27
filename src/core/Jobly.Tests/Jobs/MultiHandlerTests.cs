using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenMultipleHandlers_WhenMessageRouted_ThenBothHandlersExecute()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // ProcessJob routes the message and immediately executes one handler job
        await ProcessJob();
        // ProcessJob executes the second handler job
        await ProcessJob();

        var counter = GetMultiHandlerCounter();
        counter.CountA.ShouldBe(1);
        counter.CountB.ShouldBe(1);
    }

    [Fact]
    public async Task GivenMultipleHandlers_WhenMessageRouted_ThenMessageIsCompleted()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // Route + execute first handler
        await ProcessJob();
        // Execute second handler
        await ProcessJob();

        var message = await CreateContext().Set<Message>()
            .Where(m => m.Id == messageId)
            .FirstAsync();

        message.CurrentState.ShouldBe(State.Completed);
        message.JobCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenMultipleHandlers_WhenMessageRouted_ThenChildJobsHaveHandlerTypeSet()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        // Route the message (creates handler jobs)
        await ProcessJob();

        var handlerJobs = await CreateContext().Set<Job>()
            .Where(j => j.MessageId == messageId)
            .ToListAsync();

        handlerJobs.Count.ShouldBe(2);
        handlerJobs.ShouldContain(j => j.HandlerType!.Contains(nameof(MultiHandlerA)));
        handlerJobs.ShouldContain(j => j.HandlerType!.Contains(nameof(MultiHandlerB)));
    }
}
