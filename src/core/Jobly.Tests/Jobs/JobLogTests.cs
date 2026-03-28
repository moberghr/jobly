using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Enums;
using Jobly.Tests.TestData.Handlers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    // ==================== Lifecycle Event Logs ====================

    [Fact]
    public async Task GivenJob_WhenCreated_ThenCreatedLogExists()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var logs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId)
            .ToListAsync();

        logs.ShouldContain(l => l.EventType == "Created");
    }

    [Fact]
    public async Task GivenJob_WhenProcessed_ThenFullLifecycleLogged()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var logs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        // Full lifecycle: Created → Processing → Completed
        logs.ShouldContain(l => l.EventType == "Created");
        logs.ShouldContain(l => l.EventType == "Processing");
        logs.ShouldContain(l => l.EventType == "Completed");

        // Correct order
        var created = logs.First(l => l.EventType == "Created");
        var processing = logs.First(l => l.EventType == "Processing");
        var completed = logs.First(l => l.EventType == "Completed");
        processing.Timestamp.ShouldBeGreaterThanOrEqualTo(created.Timestamp);
        completed.Timestamp.ShouldBeGreaterThanOrEqualTo(processing.Timestamp);
    }

    [Fact]
    public async Task GivenFailedJob_WhenProcessed_ThenFailedLogWithErrorLevel()
    {
        var context = CreateContext();
        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var logs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        logs.ShouldContain(l => l.EventType == "Created");
        logs.ShouldContain(l => l.EventType == "Processing");
        logs.ShouldContain(l => l.EventType == "Failed" && l.Level == "Error");

        // Should NOT contain Completed
        logs.ShouldNotContain(l => l.EventType == "Completed");
    }

    [Fact]
    public async Task GivenDeletedJob_ThenDeletedLogExists()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.DeleteJob(jobId);

        var logs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId)
            .ToListAsync();

        logs.ShouldContain(l => l.EventType == "Deleted");
    }

    [Fact]
    public async Task GivenRequeuedJob_ThenRequeuedLogExists()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // completes

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.RequeueJob(jobId);

        var logs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId)
            .ToListAsync();

        logs.ShouldContain(l => l.EventType == "Requeued");
    }

    [Fact]
    public async Task GivenJobDetail_ThenLogsContainAllEventTypes()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // Created → Processing → Completed

        var service = TestUtils.CreateJoblyService(CreateContext());
        await service.RequeueJob(jobId); // + Requeued

        var jobDetail = await service.GetJobById(jobId);
        jobDetail.ShouldNotBeNull();

        jobDetail.Logs.ShouldContain(l => l.EventType == "Created");
        jobDetail.Logs.ShouldContain(l => l.EventType == "Processing");
        jobDetail.Logs.ShouldContain(l => l.EventType == "Completed");
        jobDetail.Logs.ShouldContain(l => l.EventType == "Requeued");

        // All logs have EventType set
        foreach (var log in jobDetail.Logs)
        {
            log.EventType.ShouldNotBeNullOrEmpty();
        }
    }

    // ==================== Handler ILogger Capture ====================

    [Fact]
    public async Task GivenHandlerWithLogging_WhenJobExecuted_ThenLogsAreCaptured()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new LoggingRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var logs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId && l.EventType == "Log")
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        logs.ShouldContain(l => l.Message.Contains("Processing logging request") && l.Level == "Information");
        logs.ShouldContain(l => l.Message.Contains("This is a warning") && l.Level == "Warning");
    }

    [Fact]
    public async Task GivenHandlerWithLogging_ThenHandlerLogsHaveLogEventType()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new LoggingRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var allLogs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        // System events have specific EventTypes
        var systemLogs = allLogs.Where(l => l.EventType != "Log").ToList();
        systemLogs.ShouldContain(l => l.EventType == "Created");
        systemLogs.ShouldContain(l => l.EventType == "Processing");
        systemLogs.ShouldContain(l => l.EventType == "Completed");

        // Handler logs have EventType = "Log"
        var handlerLogs = allLogs.Where(l => l.EventType == "Log").ToList();
        handlerLogs.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GivenHandlerThatThrows_WhenJobExecuted_ThenLogsBeforeErrorAreCaptured()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new ErrorLoggingRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var handlerLogs = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId && l.EventType == "Log")
            .ToListAsync();

        handlerLogs.ShouldContain(l => l.Message.Contains("About to fail"));
    }

    [Fact]
    public async Task GivenJobWithLogs_WhenGetJobById_ThenLogsIncludedWithEventType()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId = await publisher.Enqueue(new LoggingRequest());
        await context.SaveChangesAsync();

        await ProcessJob();

        var service = TestUtils.CreateJoblyService(CreateContext());
        var jobDetail = await service.GetJobById(jobId);

        jobDetail.ShouldNotBeNull();

        // Should have both system and handler logs
        jobDetail.Logs.ShouldContain(l => l.EventType == "Created");
        jobDetail.Logs.ShouldContain(l => l.EventType == "Log");
        jobDetail.Logs.ShouldContain(l => l.EventType == "Completed");

        // EventType is returned in the model
        foreach (var log in jobDetail.Logs)
        {
            log.EventType.ShouldNotBeNullOrEmpty();
        }
    }

    // ==================== Log Isolation ====================

    [Fact]
    public async Task GivenTwoJobs_ThenLogsDoNotLeak()
    {
        var context = CreateContext();
        var publisher = TestUtils.CreatePublisher(context);

        var jobId1 = await publisher.Enqueue(new LoggingRequest());
        var jobId2 = await publisher.Enqueue(new UnitRequest());
        await context.SaveChangesAsync();

        await ProcessJob(); // processes job1
        await ProcessJob(); // processes job2

        var logs1 = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId1 && l.EventType == "Log")
            .ToListAsync();

        var logs2 = await CreateContext().Set<JobLog>()
            .Where(l => l.JobId == jobId2 && l.EventType == "Log")
            .ToListAsync();

        // LoggingRequest handler writes logs, UnitRequest does not
        logs1.Count.ShouldBeGreaterThan(0);
        logs2.Count.ShouldBe(0);
    }
}
