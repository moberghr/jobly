using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;
using Jobly.Tests.Fixtures;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Integration;

public abstract class MutexIntegrationTestsBase : IntegrationTestBase
{
    protected MutexIntegrationTestsBase(IDatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task GivenTwoJobsWithSameMutex_WhenProcessed_ThenSecondIsCancelled()
    {
        var publisher = Server.CreatePublisher();

        // Enqueue a slow job that holds the mutex
        var job1Id = await publisher.Enqueue(new CancellableRequest(), new JobParameters { Mutex = "test-mutex" });
        await publisher.SaveChangesAsync();

        // Wait for it to start processing
        await Server.WaitForJobState(job1Id, State.Processing);

        // Enqueue a second job with the same mutex
        var publisher2 = Server.CreatePublisher();
        var job2Id = await publisher2.Enqueue(new UnitRequest(), new JobParameters { Mutex = "test-mutex" });
        await publisher2.SaveChangesAsync();

        // Wait for job2 to be picked up and cancelled
        await Server.WaitForJobState(job2Id, State.Deleted, timeout: TimeSpan.FromSeconds(10));

        // Verify job2 was cancelled due to mutex
        var logs = await Server.GetJobLogs(job2Id);
        logs.ShouldContain(l => l.EventType == "Deleted" && l.Message.Contains("mutex"));

        // Job1 should still be processing (it's the slow one)
        var job1 = await Server.GetJob(job1Id);
        job1.CurrentState.ShouldBe(State.Processing);

        // Cancel the slow job so the test doesn't wait 30s
        var cmd = Server.CreateCommandService();
        await cmd.DeleteJob(job1Id);
    }
}

[Collection<PostgreSqlIntegrationCollection>]
public class MutexIntegrationTests_PostgreSql : MutexIntegrationTestsBase
{
    public MutexIntegrationTests_PostgreSql(PostgreSqlIntegrationFixture fixture)
        : base(fixture)
    {
    }
}

[Collection<SqlServerIntegrationCollection>]
[Trait("Category", "SqlServer")]
public class MutexIntegrationTests_SqlServer : MutexIntegrationTestsBase
{
    public MutexIntegrationTests_SqlServer(SqlServerIntegrationFixture fixture)
        : base(fixture)
    {
    }
}
