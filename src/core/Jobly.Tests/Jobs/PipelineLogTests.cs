using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenPipelineBehaviorWithLogger_WhenJobProcessed_ThenPipelineLogsAppearInJobLogs()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);
        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var job = await GetJob(jobId);
        job.CurrentState.ShouldBe(State.Completed);

        var handlerLogs = await CreateContext().Set<JobLog>()
            .Where(x => x.JobId == jobId && x.EventType == "Log")
            .ToListAsync();

        handlerLogs.ShouldContain(l => l.Message.Contains("Pipeline before handler"));
        handlerLogs.ShouldContain(l => l.Message.Contains("Pipeline after handler"));
    }
}
