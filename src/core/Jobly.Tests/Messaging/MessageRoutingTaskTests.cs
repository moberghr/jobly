using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobly.Tests.Messaging;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class MessageRoutingTaskTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected MessageRoutingTaskTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(MessageRoutingTaskTestsBase).Assembly);
        services.AddSingleton<MultiHandlerCounter>();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(MessageRoutingTaskTestsBase).Assembly);
        services.AddLogging();
        services.AddScoped<TestContext>(_ => _fixture.CreateContext());

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    [TimedFact]
    public async Task RunMessageRouting_WithEnqueuedMessage_CreatesChildJobs()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = typeof(SingleHandlerMessage).AssemblyQualifiedName,
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory();

        // Act
        var routeCtx = _fixture.CreateContext();
        var routed = await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        routed.ShouldBeGreaterThan(0);
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync();
        children.Count.ShouldBeGreaterThan(0);
    }

    [TimedFact]
    public async Task RunMessageRouting_SetsMessageToProcessing()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = typeof(SingleHandlerMessage).AssemblyQualifiedName,
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory();

        // Act
        var routeCtx = _fixture.CreateContext();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var message = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == messageId);
        message.ShouldNotBeNull();
        message.CurrentState.ShouldBe(State.Processing);
    }

    [TimedFact]
    public async Task RunMessageRouting_ChildJobsInheritQueue()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = typeof(SingleHandlerMessage).AssemblyQualifiedName,
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "critical",
        });
        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory();

        // Act
        var routeCtx = _fixture.CreateContext();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync();
        children.ShouldAllBe(c => c.Queue == "critical");
    }

    [TimedFact]
    public async Task RunMessageRouting_NoMessages_ReturnsZero()
    {
        // Arrange — empty DB
        var scopeFactory = BuildScopeFactory();

        // Act
        var ctx = _fixture.CreateContext();
        var routed = await MessageRoutingTask<TestContext>.RunMessageRouting(ctx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        routed.ShouldBe(0);
    }

    [TimedFact]
    public async Task RunMessageRouting_MultipleMessages_RoutesAll()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var messageIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var messageId = Guid.NewGuid();
            messageIds.Add(messageId);
            ctx.Set<Job>().Add(new Job
            {
                Id = messageId,
                Kind = JobKind.Message,
                CurrentState = State.Enqueued,
                Type = typeof(SingleHandlerMessage).AssemblyQualifiedName,
                Message = "{}",
                CreateTime = DateTime.UtcNow,
                ScheduleTime = DateTime.UtcNow,
                Queue = "default",
            });
        }

        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory();

        // Act
        var routeCtx = _fixture.CreateContext();
        var routed = await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        routed.ShouldBe(3);
        var readCtx = _fixture.CreateContext();
        foreach (var mid in messageIds)
        {
            var msg = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == mid);
            msg.ShouldNotBeNull();
            msg.CurrentState.ShouldBe(State.Processing);
        }
    }

    /// <summary>
    /// HIGH: No JobLog when message routing finds 0 handlers.
    /// Messages marked Failed should have a log explaining why.
    /// </summary>
    [TimedFact]
    public async Task MessageRouting_NoHandlers_CreatesFailedLogEntry()
    {
        // Arrange: a message with a type that has no registered handlers
        var ctx = _fixture.CreateContext();
        var messageId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = messageId,
            Kind = JobKind.Message,
            CurrentState = State.Enqueued,
            Type = "NonExistent.Type, NonExistent.Assembly",
            Message = "{}",
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        // Act: run message routing
        var routeCtx = _fixture.CreateContext();
        var scopeFactory = CreateScopeFactory();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert: message should be Failed with a log entry explaining why
        var readCtx = _fixture.CreateContext();
        var message = await readCtx.Set<Job>().FindAsync(messageId);
        message.ShouldNotBeNull();
        message.CurrentState.ShouldBe(State.Failed);

        var logs = await readCtx.Set<JobLog>().Where(l => l.JobId == messageId).ToListAsync();
        logs.ShouldNotBeEmpty("Failed message should have a log entry explaining the failure");
        logs.ShouldContain(l => l.EventType == "Failed");
    }
}
