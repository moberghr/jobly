using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Warp.Core.Data.Queries;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Handlers;
using Warp.Core.Handlers.Generated;
using Warp.Tests.Fixtures;
using Warp.Tests.Helpers;
using Warp.Tests.TestData.Handlers;
using Warp.Worker.Services;

namespace Warp.Tests.Messaging;

[GenerateDatabaseTests]
public abstract class MessageRoutingErrorTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected MessageRoutingErrorTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IServiceScopeFactory BuildScopeFactoryWithHandlers()
    {
        var services = new ServiceCollection();
        services.AddWarpMediator();
        services.AddSingleton<MultiHandlerCounter>();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static IServiceScopeFactory BuildEmptyScopeFactory()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    [TimedFact]
    public async Task RunMessageRouting_UnknownMessageType_SetsMessageToFailed()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = "NonExistent.Type, FakeAssembly",
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var scopeFactory = BuildScopeFactoryWithHandlers();

        // Act
        var routeCtx = _fixture.CreateContext();
        var task = TestTasks.CreateMessageRouter(routeCtx, scopeFactory, TimeProvider.System);
        await task.RunMessageRoutingAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var message = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == messageId, Xunit.TestContext.Current.CancellationToken);
        message.ShouldNotBeNull();
        message.CurrentState.ShouldBe(State.Failed);
    }

    [TimedFact]
    public async Task RunMessageRouting_NoHandlersRegistered_SetsMessageToFailed()
    {
        // Arrange — use a valid type that exists but has no handler registered
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = typeof(MultiRequest).AssemblyQualifiedName,
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        // Empty scope factory — no handlers registered
        var scopeFactory = BuildEmptyScopeFactory();

        // Act
        var routeCtx = _fixture.CreateContext();
        var task = TestTasks.CreateMessageRouter(routeCtx, scopeFactory, TimeProvider.System);
        await task.RunMessageRoutingAsync(CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var message = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == messageId, Xunit.TestContext.Current.CancellationToken);
        message.ShouldNotBeNull();
        message.CurrentState.ShouldBe(State.Failed);
    }

    [TimedFact]
    public async Task RunMessageRouting_MultipleHandlers_CreatesOneJobPerHandler()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = typeof(MultiRequest).AssemblyQualifiedName,
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var scopeFactory = BuildScopeFactoryWithHandlers();

        // Act
        var routeCtx = _fixture.CreateContext();
        var task = TestTasks.CreateMessageRouter(routeCtx, scopeFactory, TimeProvider.System);
        await task.RunMessageRoutingAsync(CancellationToken.None);

        // Assert — MultiRequest has MultiHandlerA and MultiHandlerB => 2 child jobs
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);
        children.Count.ShouldBe(2);
    }

    [TimedFact]
    public async Task RunMessageRouting_SetsHandlerTypeOnChildJobs()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = typeof(MultiRequest).AssemblyQualifiedName,
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var scopeFactory = BuildScopeFactoryWithHandlers();

        // Act
        var routeCtx = _fixture.CreateContext();
        var task = TestTasks.CreateMessageRouter(routeCtx, scopeFactory, TimeProvider.System);
        await task.RunMessageRoutingAsync(CancellationToken.None);

        // Assert — each child should have a distinct HandlerType
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync(Xunit.TestContext.Current.CancellationToken);

        children.Count.ShouldBe(2);
        children.ShouldAllBe(c => !string.IsNullOrEmpty(c.HandlerType));

        var handlerTypes = children.Select(c => c.HandlerType).Distinct().ToList();
        handlerTypes.Count.ShouldBe(2);
    }
}
