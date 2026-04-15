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

public abstract class MessageRoutingErrorTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected MessageRoutingErrorTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IServiceScopeFactory BuildScopeFactoryWithHandlers()
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(MessageRoutingErrorTestsBase).Assembly);
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

    [Fact]
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
        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactoryWithHandlers();

        // Act
        var routeCtx = _fixture.CreateContext();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var message = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == messageId);
        message.ShouldNotBeNull();
        message.CurrentState.ShouldBe(State.Failed);
    }

    [Fact]
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
        await ctx.SaveChangesAsync();

        // Empty scope factory — no handlers registered
        var scopeFactory = BuildEmptyScopeFactory();

        // Act
        var routeCtx = _fixture.CreateContext();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var message = await readCtx.Set<Job>().FirstOrDefaultAsync(j => j.Id == messageId);
        message.ShouldNotBeNull();
        message.CurrentState.ShouldBe(State.Failed);
    }

    [Fact]
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
        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactoryWithHandlers();

        // Act
        var routeCtx = _fixture.CreateContext();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert — MultiRequest has MultiHandlerA and MultiHandlerB => 2 child jobs
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync();
        children.Count.ShouldBe(2);
    }

    [Fact]
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
        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactoryWithHandlers();

        // Act
        var routeCtx = _fixture.CreateContext();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert — each child should have a distinct HandlerType
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(j => j.ParentJobId == messageId && j.Kind == JobKind.Job)
            .ToListAsync();

        children.Count.ShouldBe(2);
        children.ShouldAllBe(c => !string.IsNullOrEmpty(c.HandlerType));

        var handlerTypes = children.Select(c => c.HandlerType).Distinct().ToList();
        handlerTypes.Count.ShouldBe(2);
    }
}

[Collection<PostgreSqlCollection>]
public class MessageRoutingErrorTests_PostgreSql : MessageRoutingErrorTestsBase
{
    public MessageRoutingErrorTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class MessageRoutingErrorTests_SqlServer : MessageRoutingErrorTestsBase
{
    public MessageRoutingErrorTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
