using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenMessageWithSingleHandler_WhenRouted_ThenOneChildJobCreated()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await RouteMessages();

        var children = await GetJobsForMessage(messageId);
        children.Count.ShouldBe(1);
        children[0].Kind.ShouldBe(JobKind.Job);
        children[0].CurrentState.ShouldBe(State.Enqueued);
    }

    [Fact]
    public async Task GivenMessageWithMultipleHandlers_WhenRouted_ThenNChildJobsCreated()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId = await publisher.Publish(new MultiRequest());
        await context.SaveChangesAsync();

        await RouteMessages();

        var children = await GetJobsForMessage(messageId);
        children.Count.ShouldBe(2);
        children.ShouldAllBe(j => j.Kind == JobKind.Job);
        children.ShouldAllBe(j => j.CurrentState == State.Enqueued);
    }

    [Fact]
    public async Task GivenMessage_WhenRouted_ThenMessageTransitionsToProcessing()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await RouteMessages();

        var message = await GetMessage(messageId);
        message.CurrentState.ShouldBe(State.Processing);
    }

    [Fact]
    public async Task GivenMessage_WhenRouted_ThenChildJobsInheritHandlerType()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await RouteMessages();

        var children = await GetJobsForMessage(messageId);
        children[0].HandlerType.ShouldNotBeNull();
        children[0].HandlerType.ShouldContain(nameof(SingleMessageHandler));
    }

    [Fact]
    public async Task GivenMessage_WhenRouted_ThenChildJobsInheritQueueAndTrace()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var messageId = await publisher.Publish(new SingleHandlerMessage(), "a-critical");
        await context.SaveChangesAsync();

        await RouteMessages();

        var message = await GetMessage(messageId);
        var children = await GetJobsForMessage(messageId);

        children[0].Queue.ShouldBe("a-critical");
        children[0].TraceId.ShouldBe(message.TraceId);
    }

    [Fact]
    public async Task GivenMultipleMessages_WhenRouted_ThenAllAreProcessed()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var msg1 = await publisher.Publish(new SingleHandlerMessage());
        var msg2 = await publisher.Publish(new SingleHandlerMessage());
        var msg3 = await publisher.Publish(new SingleHandlerMessage());
        await context.SaveChangesAsync();

        await RouteMessages();

        (await GetMessage(msg1)).CurrentState.ShouldBe(State.Processing);
        (await GetMessage(msg2)).CurrentState.ShouldBe(State.Processing);
        (await GetMessage(msg3)).CurrentState.ShouldBe(State.Processing);
    }

    [Fact]
    public async Task GivenNoMessages_WhenRoutingRuns_ThenNothingHappens()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        await publisher.Enqueue(new UnitRequest()); // only a regular job, no messages
        await context.SaveChangesAsync();

        await RouteMessages();

        var messageCount = await CreateContext().Set<Job>()
            .Where(x => x.Kind == JobKind.Message && x.CurrentState == State.Processing)
            .CountAsync();
        messageCount.ShouldBe(0);
    }
}
