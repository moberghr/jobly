using Jobly.Core;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenServerRegistered_WhenQueried_ThenServerAppearsInList()
    {
        var context = CreateContext();

        await TestUtils.RegisterTestServer(context, workerCount: 5);

        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var servers = await service.GetServers();

        servers.Count.ShouldBe(1);
        servers[0].Id.ShouldBe(TestUtils.TestServerId);
        servers[0].ServiceCount.ShouldBe(5);
        servers[0].Workers.Count.ShouldBe(1);
        servers[0].Workers[0].WorkerId.ShouldBe(TestUtils.TestWorkerId);
    }

    [Fact]
    public async Task GivenServerRegistered_WhenDashboardQueried_ThenServerCountIsOne()
    {
        await EnsureServerRegistered();

        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var stats = await service.GetJoblyStatus();

        stats.Servers.ShouldBe(1);
    }

    [Fact]
    public async Task GivenServerProcessingJob_WhenQueried_ThenWorkerIdIsClearedOnCompletedJob()
    {
        await EnsureServerRegistered();

        var context = CreateContext();
        var testLogId = await CreateLogInDb(context);
        var jobId = await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentWorkerId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenServerProcessingJob_WhenQueried_ThenWorkerAppearsInServerData()
    {
        await EnsureServerRegistered();

        var context = CreateContext();
        var testLogId = await CreateLogInDb(context);
        await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var servers = await service.GetServers();

        servers.Count.ShouldBe(1);
        servers[0].Workers.Count.ShouldBeGreaterThan(0);
        servers[0].Workers[0].WorkerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task GivenCompletedJob_WhenQueried_ThenWorkerHasNoActiveJob()
    {
        await EnsureServerRegistered();

        var context = CreateContext();
        var testLogId = await CreateLogInDb(context);
        await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        var service = TestUtils.CreateDashboardStatsService(CreateContext());
        var servers = await service.GetServers();

        servers[0].Workers.Count.ShouldBeGreaterThan(0);
        servers[0].Workers[0].CurrentJobId.ShouldBeNull();
    }
}
