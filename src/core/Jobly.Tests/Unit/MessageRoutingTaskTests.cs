using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobly.Tests.Unit;

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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
}

[Collection<PostgreSqlCollection>]
public class MessageRoutingTaskTests_PostgreSql : MessageRoutingTaskTestsBase
{
    public MessageRoutingTaskTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class MessageRoutingTaskTests_SqlServer : MessageRoutingTaskTestsBase
{
    public MessageRoutingTaskTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
