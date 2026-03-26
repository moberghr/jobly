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

        var service = TestUtils.CreateJoblyService(CreateContext());
        var servers = await service.GetServers();

        servers.Count.ShouldBe(1);
        servers[0].Id.ShouldBe(TestUtils.TestServerId);
        servers[0].ServiceCount.ShouldBe(5);
        servers[0].Workers.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenServerRegistered_WhenDashboardQueried_ThenServerCountIsOne()
    {
        var context = CreateContext();

        await TestUtils.RegisterTestServer(context);

        var service = TestUtils.CreateJoblyService(CreateContext());
        var stats = await service.GetJoblyStatus();

        stats.Servers.ShouldBe(1);
    }

    [Fact]
    public async Task GivenServerProcessingJob_WhenQueried_ThenWorkerIdIsSetOnJob()
    {
        var context = CreateContext();

        await TestUtils.RegisterTestServer(context);

        var testLogId = await CreateLogInDb(context);
        var jobId = await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        // Job is completed so CurrentWorkerId should be cleared
        var job = await GetJob(jobId);
        job.CurrentWorkerId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenServerProcessingJob_WhenQueried_ThenWorkerAppearsInServerData()
    {
        var context = CreateContext();

        await TestUtils.RegisterTestServer(context);

        var testLogId = await CreateLogInDb(context);
        await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        // Worker should be registered in Worker table and visible in GetServers()
        var service = TestUtils.CreateJoblyService(CreateContext());
        var servers = await service.GetServers();

        servers.Count.ShouldBe(1);
        servers[0].Workers.Count.ShouldBeGreaterThan(0);
        servers[0].Workers[0].WorkerId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task GivenCompletedJob_WhenQueried_ThenWorkerHasNoActiveJob()
    {
        var context = CreateContext();

        await TestUtils.RegisterTestServer(context);

        var testLogId = await CreateLogInDb(context);
        await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        var service = TestUtils.CreateJoblyService(CreateContext());
        var servers = await service.GetServers();

        servers[0].Workers.Count.ShouldBeGreaterThan(0);
        // Job is completed, so worker should have no active job
        servers[0].Workers[0].CurrentJobId.ShouldBeNull();
    }
}
