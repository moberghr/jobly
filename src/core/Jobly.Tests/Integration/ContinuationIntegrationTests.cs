using System.Collections.Concurrent;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class ContinuationIntegrationTestsBase : IAsyncLifetime
{
    private static readonly ConcurrentDictionary<Type, JoblyTestServer> _servers = new();
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IDatabaseFixture _fixture;
    protected JoblyTestServer _server = null!;

    protected ContinuationIntegrationTestsBase(IDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var key = _fixture.GetType();
        if (!_servers.TryGetValue(key, out var server))
        {
            await _lock.WaitAsync();
            try
            {
                if (!_servers.TryGetValue(key, out server))
                {
                    server = await JoblyTestServer.StartAsync(_fixture);
                    _servers[key] = server;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        await _fixture.ResetAsync();
        _server = server;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenParentJob_WhenCompletes_ThenChildActivatesAndCompletes()
    {
        var publisher = _server.CreatePublisher();
        var parentId = await publisher.Enqueue(new UnitRequest());
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await publisher.SaveChangesAsync();

        await _server.WaitForCompletion();

        var ctx = _server.CreateContext();

        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId);
        parent.CurrentState.ShouldBe(State.Completed);

        var child = await ctx.Set<Job>().FirstAsync(j => j.Id == childId);
        child.CurrentState.ShouldBe(State.Completed);
        child.ParentJobId.ShouldBe(parentId);
    }

    [Fact]
    public async Task GivenParentJobThatFails_WhenDefaultOnlyOnSucceeded_ThenChildStaysAwaiting()
    {
        var publisher = _server.CreatePublisher();
        var parentId = await publisher.Enqueue(new ThrowExceptionRequest());
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await publisher.SaveChangesAsync();

        // Wait for parent to fail — orchestrator runs every 100ms, so by the time this
        // returns the orchestrator has already processed this parent's children
        await _server.WaitForJobState(parentId, State.Failed, timeout: TimeSpan.FromSeconds(15));

        var ctx = _server.CreateContext();

        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId);
        parent.CurrentState.ShouldBe(State.Failed);

        // Child should remain awaiting — default continuation is OnlyOnSucceeded
        var child = await ctx.Set<Job>().FirstAsync(j => j.Id == childId);
        child.CurrentState.ShouldBe(State.Awaiting);
    }

    [Fact]
    public async Task GivenParentJobThatFails_WithOnAnyFinishedState_ThenChildStillActivates()
    {
        // Use batch publisher to create a batch with OnAnyFinishedState continuation,
        // since individual job continuations don't have ContinuationOptions on the child.
        // Batch is the mechanism for continuation options.
        var batchPublisher = _server.CreateBatchPublisher();

        var batchJobs = new List<ThrowExceptionRequest> { new() };
        var batchId = await batchPublisher.StartNew(batchJobs, options: ContinuationOptions.OnAnyFinishedState);

        var continuationJobs = new List<UnitRequest> { new() };
        var continuationBatchId = await batchPublisher.ContinueBatchWith(continuationJobs, batchId);

        await batchPublisher.SaveChangesAsync();

        await _server.WaitForCompletion();

        var ctx = _server.CreateContext();

        // Batch with OnAnyFinishedState completes even when children fail
        var batch = await ctx.Set<Job>().FirstAsync(j => j.Id == batchId);
        batch.CurrentState.ShouldBe(State.Completed);

        // Continuation batch activated and completed because OnAnyFinishedState
        var continuation = await ctx.Set<Job>().FirstAsync(j => j.Id == continuationBatchId);
        continuation.CurrentState.ShouldBe(State.Completed);

        var continuationChildren = await ctx.Set<Job>()
            .Where(j => j.ParentJobId == continuationBatchId && j.Kind == JobKind.Job)
            .ToListAsync();
        continuationChildren.ShouldAllBe(j => j.CurrentState == State.Completed);
    }

    [Fact]
    public async Task GivenThreeLevelContinuationChain_WhenProcessed_ThenAllComplete()
    {
        var publisher = _server.CreatePublisher();
        var grandparentId = await publisher.Enqueue(new UnitRequest());
        var parentId = await publisher.Enqueue(new UnitRequest(), grandparentId);
        var childId = await publisher.Enqueue(new UnitRequest(), parentId);
        await publisher.SaveChangesAsync();

        await _server.WaitForCompletion();

        var ctx = _server.CreateContext();

        var grandparent = await ctx.Set<Job>().FirstAsync(j => j.Id == grandparentId);
        grandparent.CurrentState.ShouldBe(State.Completed);

        var parent = await ctx.Set<Job>().FirstAsync(j => j.Id == parentId);
        parent.CurrentState.ShouldBe(State.Completed);

        var child = await ctx.Set<Job>().FirstAsync(j => j.Id == childId);
        child.CurrentState.ShouldBe(State.Completed);
    }
}

[Collection("PostgreSql")]
public class ContinuationIntegrationTests_PostgreSql : ContinuationIntegrationTestsBase
{
    public ContinuationIntegrationTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture) { }
}

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class ContinuationIntegrationTests_SqlServer : ContinuationIntegrationTestsBase
{
    public ContinuationIntegrationTests_SqlServer(SqlServerFixture fixture) : base(fixture) { }
}
