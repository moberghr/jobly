using Jobly.Core.Data.Entities;
using Jobly.Tests.Fixtures;
using Jobly.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Unit;

public abstract class RecurringJobLogCleanupTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RecurringJobLogCleanupTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task CleanupRecurringJobLogs_KeepsLast100PerRecurringJob()
    {
        // Arrange: 150 logs for recurring job 1, 50 for recurring job 2
        var ctx = _fixture.CreateContext();

        var rj1 = new RecurringJob { Name = "rj-1", Cron = "* * * * *", CreatedAt = DateTime.UtcNow };
        var rj2 = new RecurringJob { Name = "rj-2", Cron = "* * * * *", CreatedAt = DateTime.UtcNow };
        ctx.Set<RecurringJob>().AddRange(rj1, rj2);
        await ctx.SaveChangesAsync();

        for (var i = 0; i < 150; i++)
        {
            ctx.Set<RecurringJobLog>().Add(new RecurringJobLog
            {
                RecurringJobId = rj1.Id,
                CreatedAt = DateTime.UtcNow.AddMinutes(-150 + i),
            });
        }

        for (var i = 0; i < 50; i++)
        {
            ctx.Set<RecurringJobLog>().Add(new RecurringJobLog
            {
                RecurringJobId = rj2.Id,
                CreatedAt = DateTime.UtcNow.AddMinutes(-50 + i),
            });
        }

        await ctx.SaveChangesAsync();

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.CleanupRecurringJobLogs(cleanCtx);

        // Assert
        var readCtx = _fixture.CreateContext();
        var rj1Count = await readCtx.Set<RecurringJobLog>().CountAsync(l => l.RecurringJobId == rj1.Id);
        var rj2Count = await readCtx.Set<RecurringJobLog>().CountAsync(l => l.RecurringJobId == rj2.Id);

        rj1Count.ShouldBe(100, "Should keep only last 100 for rj1");
        rj2Count.ShouldBe(50, "Should keep all 50 for rj2 (under limit)");
    }

    [TimedFact]
    public async Task CleanupRecurringJobLogs_DeletesOldestEntries()
    {
        // Arrange: 110 logs, verify the 10 oldest are deleted
        var ctx = _fixture.CreateContext();
        var rj = new RecurringJob { Name = "oldest-test", Cron = "* * * * *", CreatedAt = DateTime.UtcNow };
        ctx.Set<RecurringJob>().Add(rj);
        await ctx.SaveChangesAsync();

        for (var i = 0; i < 110; i++)
        {
            ctx.Set<RecurringJobLog>().Add(new RecurringJobLog
            {
                RecurringJobId = rj.Id,
                CreatedAt = DateTime.UtcNow.AddMinutes(-110 + i),
            });
        }

        await ctx.SaveChangesAsync();

        // Get the newest entry's CreatedAt before cleanup
        var newestBefore = await _fixture.CreateContext().Set<RecurringJobLog>()
            .Where(l => l.RecurringJobId == rj.Id)
            .MaxAsync(l => l.CreatedAt);

        // Act
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.CleanupRecurringJobLogs(cleanCtx);

        // Assert: oldest entry should now be the 11th from the original set
        var readCtx = _fixture.CreateContext();
        var oldest = await readCtx.Set<RecurringJobLog>()
            .Where(l => l.RecurringJobId == rj.Id)
            .MinAsync(l => l.CreatedAt);

        var count = await readCtx.Set<RecurringJobLog>()
            .Where(l => l.RecurringJobId == rj.Id)
            .CountAsync();

        count.ShouldBe(100);
        oldest.ShouldBeGreaterThan(newestBefore.AddMinutes(-100), "Oldest surviving entry should be within the last 100");
    }

    [TimedFact]
    public async Task CleanupRecurringJobLogs_NoLogsDoesNothing()
    {
        var cleanCtx = _fixture.CreateContext();
        await ExpirationCleanupTask<TestContext>.CleanupRecurringJobLogs(cleanCtx);

        var readCtx = _fixture.CreateContext();
        var count = await readCtx.Set<RecurringJobLog>().CountAsync();
        count.ShouldBe(0);
    }
}

[Collection<PostgreSqlCollection>]
public class RecurringJobLogCleanupTests_PostgreSql : RecurringJobLogCleanupTestsBase
{
    public RecurringJobLogCleanupTests_PostgreSql(PostgreSqlFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerCollection>]
[Trait("Category", "SqlServer")]
public class RecurringJobLogCleanupTests_SqlServer : RecurringJobLogCleanupTestsBase
{
    public RecurringJobLogCleanupTests_SqlServer(SqlServerFixture fixture)
        : base(fixture)
    {
    }
}
