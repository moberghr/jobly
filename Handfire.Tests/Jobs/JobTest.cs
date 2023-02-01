using System.Text.Json;
using Handfire.Core;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Tests.TestData.Handlers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Handfire.Tests.Jobs;

public class JobTest : PostgreSqlTestBase
{
    [Fact]
    public async Task Publish_AddJob_ShouldHaveCreatedStatusInDb()
    {
        var context = CreateContext();

        var logInDb = new TestLog();
        await context.TestLogs.AddAsync(logInDb);
        await context.SaveChangesAsync();

        var publisher = new Publisher<TestContext>(context);
        var processLogJob = new PrecessLogRequest { TestTaskId = logInDb.Id };
        var jobName = Guid.NewGuid().ToString();

        await publisher.Publish(jobName, processLogJob);

        await context.SaveChangesAsync();

        var jobFromDb = await GetJobWithStatesByName(context, jobName);

        Assert.NotNull(jobFromDb);
        Assert.Equal(State.Created, jobFromDb.CurrentState);
        Assert.Equal(processLogJob.GetType().AssemblyQualifiedName!, jobFromDb.Type);
        Assert.Equal(JsonSerializer.Serialize(processLogJob), jobFromDb.Message);

        Assert.Single(jobFromDb.JobStates);
        Assert.Equal(State.Created, jobFromDb.JobStates.Single().State);
    }

    [Fact]
    public async Task GetAndProcessJob_ProcessCreatedJob_ShouldBeCompleted()
    {
        var context = CreateContext();

        var testLogId = await CreateLogInDb(context);

        var jobName = await CreateJob(context, testLogId);

        await ProcessJob();

        var jobFromDb = await GetJobWithStatesByName(context, jobName);
        var logFromDb = await GetTestLog(context, testLogId);
        
        Assert.Equal(State.Completed, jobFromDb.CurrentState);
        Assert.Equal(2, jobFromDb.JobStates.Count);
        Assert.Equal(State.Completed, jobFromDb.JobStates.Last().State);

        Assert.NotNull(logFromDb.ProcessedTime);
    }
}
