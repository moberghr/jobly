using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenHandlerWithLogging_WhenJobExecuted_ThenLogsAreCaptured()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new LoggingRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var logs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        logs.Count.ShouldBeGreaterThanOrEqualTo(2);
        logs.ShouldContain(l => l.Message.Contains("Processing logging request") && l.Level == "Information");
        logs.ShouldContain(l => l.Message.Contains("This is a warning") && l.Level == "Warning");
    }

    [Fact]
    public async Task GivenHandlerThatThrows_WhenJobExecuted_ThenLogsBeforeErrorAreCaptured()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new ErrorLoggingRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var logs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        logs.ShouldContain(l => l.Message.Contains("About to fail"));
    }

    [Fact]
    public async Task GivenJobWithLogs_WhenGetJobById_ThenLogsIncluded()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new LoggingRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var service = TestUtils.CreateJoblyService(CreateContext());
        var jobDetail = await service.GetJobById(jobId);

        jobDetail.ShouldNotBeNull();
        jobDetail.Logs.Count.ShouldBeGreaterThanOrEqualTo(2);
    }
}
