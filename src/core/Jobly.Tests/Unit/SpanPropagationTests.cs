using System.Diagnostics;
using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Handlers;
using Jobly.Core.Logging;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class SpanPropagationTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected SpanPropagationTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Publisher<TestContext> CreatePublisher(TestContext ctx)
    {
        return new Publisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, new ServiceCollection().BuildServiceProvider());
    }

    private static BatchPublisher<TestContext> CreateBatchPublisher(TestContext ctx)
    {
        return new BatchPublisher<TestContext>(ctx, Options.Create(new JoblyConfiguration()), TimeProvider.System, new ServiceCollection().BuildServiceProvider());
    }

    private static IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddHandlers(typeof(SpanPropagationTestsBase).Assembly);
        services.AddSingleton<MultiHandlerCounter>();
        var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    // --- Publisher.Enqueue ---
    [TimedFact]
    public async Task Enqueue_WithActiveActivity_CapturesParentSpanId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        using var activity = new Activity("test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        // Act
        var id = await publisher.Enqueue(new UnitRequest());
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>()
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync();

        job.ShouldNotBeNull();
        job.ParentSpanId.ShouldBe(activity.SpanId.ToHexString());
    }

    [TimedFact]
    public async Task Enqueue_WithoutActivity_ParentSpanIdIsNull()
    {
        // Arrange
        var previousActivity = Activity.Current;
        Activity.Current = null;

        try
        {
            var ctx = _fixture.CreateContext();
            var publisher = CreatePublisher(ctx);

            // Act
            var id = await publisher.Enqueue(new UnitRequest());
            await ctx.SaveChangesAsync();

            // Assert
            var readCtx = _fixture.CreateContext();
            var job = await readCtx.Set<Job>()
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();

            job.ShouldNotBeNull();
            job.ParentSpanId.ShouldBeNull();
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    // --- Publisher.Publish (Message) ---
    [TimedFact]
    public async Task Publish_WithActiveActivity_CapturesParentSpanId()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        using var activity = new Activity("test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        // Act
        var id = await publisher.Publish(new SingleHandlerMessage());
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>()
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync();

        job.ShouldNotBeNull();
        job.ParentSpanId.ShouldBe(activity.SpanId.ToHexString());
    }

    // --- BatchPublisher ---
    [TimedFact]
    public async Task StartBatch_WithActiveActivity_CapturesParentSpanIdOnBatchAndChildren()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var batchPublisher = CreateBatchPublisher(ctx);

        using var activity = new Activity("test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        // Act
        var batchId = await batchPublisher.StartNew(new List<UnitRequest>
        {
            new(),
            new(),
        });
        await ctx.SaveChangesAsync();

        // Assert
        var readCtx = _fixture.CreateContext();
        var batch = await readCtx.Set<Job>()
            .Where(x => x.Id == batchId)
            .FirstOrDefaultAsync();

        batch.ShouldNotBeNull();
        batch.ParentSpanId.ShouldBe(activity.SpanId.ToHexString());

        var children = await readCtx.Set<Job>()
            .Where(x => x.ParentJobId == batchId)
            .ToListAsync();

        children.Count.ShouldBe(2);
        foreach (var child in children)
        {
            child.ParentSpanId.ShouldBe(activity.SpanId.ToHexString());
        }
    }

    // --- MessageRoutingTask propagation ---
    [TimedFact]
    public async Task MessageRouting_PropagatesParentSpanIdToChildJobs()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var parentSpanId = ActivitySpanId.CreateRandom().ToHexString();
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
            TraceId = Guid.NewGuid(),
            ParentSpanId = parentSpanId,
        });
        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory();

        // Act
        var routeCtx = _fixture.CreateContext();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(x => x.ParentJobId == messageId)
            .Where(x => x.Kind == JobKind.Job)
            .ToListAsync();

        children.Count.ShouldBeGreaterThan(0);
        foreach (var child in children)
        {
            child.ParentSpanId.ShouldBe(parentSpanId);
        }
    }

    [TimedFact]
    public async Task MessageRouting_MessageWithNullParentSpanId_ChildJobsHaveNullParentSpanId()
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
            TraceId = Guid.NewGuid(),
            ParentSpanId = null,
        });
        await ctx.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory();

        // Act
        var routeCtx = _fixture.CreateContext();
        await MessageRoutingTask<TestContext>.RunMessageRouting(routeCtx, scopeFactory, TimeProvider.System, CancellationToken.None);

        // Assert
        var readCtx = _fixture.CreateContext();
        var children = await readCtx.Set<Job>()
            .Where(x => x.ParentJobId == messageId)
            .Where(x => x.Kind == JobKind.Job)
            .ToListAsync();

        children.Count.ShouldBeGreaterThan(0);
        foreach (var child in children)
        {
            child.ParentSpanId.ShouldBeNull();
        }
    }

    // --- Publisher inside handler (JobExecutionContext active) ---
    [TimedFact]
    public async Task Enqueue_InsideHandlerWithActivity_CapturesHandlerSpanId()
    {
        // Arrange — simulate handler execution context with an active Activity
        var ctx = _fixture.CreateContext();
        var publisher = CreatePublisher(ctx);

        using var activity = new Activity("Jobly.Execute")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        var parentTraceId = Guid.NewGuid();
        JobExecutionContext.Current = new JobExecutionInfo
        {
            JobId = Guid.NewGuid(),
            TraceId = parentTraceId,
            MetadataJson = null,
        };

        try
        {
            // Act
            var id = await publisher.Enqueue(new UnitRequest());
            await ctx.SaveChangesAsync();

            // Assert
            var readCtx = _fixture.CreateContext();
            var job = await readCtx.Set<Job>()
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();

            job.ShouldNotBeNull();
            job.ParentSpanId.ShouldBe(activity.SpanId.ToHexString());
            job.TraceId.ShouldBe(parentTraceId);
        }
        finally
        {
            JobExecutionContext.Current = null;
            activity.Stop();
        }
    }
}

[Collection<PostgreSqlCollection>]
[Trait("Category", "PostgreSql")]
public class SpanPropagationTests_PostgreSql : SpanPropagationTestsBase
{
    public SpanPropagationTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class SpanPropagationTests_SqlServer : SpanPropagationTestsBase
{
    public SpanPropagationTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
