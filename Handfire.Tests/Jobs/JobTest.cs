using System.Text.Json;
using Handfire.Core;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Tests.TestData;
using Handfire.Tests.TestData.Handlers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Handfire.Tests.Jobs;

public class JobTest : SqlServerTestBase
{
    [Fact]
    public async Task Publish_AddJob_ShouldHaveCreatedStatusInDb()
    {
        var context = CreateContext();

        var logInDb = new TestLog();
        await context.TestLogs.AddAsync(logInDb);
        await context.SaveChangesAsync();

        var publisher = new Publisher<TestContext>(context);
        var processLogJobRequest = new PrecessLogRequest { TestTaskId = logInDb.Id };

        var jobId = await publisher.Publish(processLogJobRequest);

        await context.SaveChangesAsync();

        var jobFromDb = await GetJobWithStates(context, jobId);

        Assert.NotNull(jobFromDb);
        Assert.Equal(State.Enqueued, jobFromDb.CurrentState);
        Assert.Equal(processLogJobRequest.GetType().AssemblyQualifiedName!, jobFromDb.Type);
        Assert.Equal(JsonSerializer.Serialize(processLogJobRequest), jobFromDb.Message);

        Assert.Single(jobFromDb.JobStates);
        Assert.Equal(State.Enqueued, jobFromDb.JobStates.Single().State);
    }

    [Fact]
    public async Task GetAndProcessJob_ProcessCreatedJob_ShouldBeCompleted()
    {
        var context = CreateContext();

        var testLogId = await CreateLogInDb(context);

        var jobId = await CreateJob(context, testLogId);

        await ProcessJob();

        var jobFromDb = await GetJobWithStates(context, jobId);
        var logFromDb = await GetTestLog(context, testLogId);
        
        Assert.Equal(State.Completed, jobFromDb.CurrentState);
        Assert.Equal(2, jobFromDb.JobStates.Count);
        Assert.Equal(State.Completed, jobFromDb.JobStates.Last().State);

        Assert.NotNull(logFromDb.ProcessedTime);
    }
}
