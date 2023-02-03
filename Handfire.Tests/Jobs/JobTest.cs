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

        var publisher = new Publisher<TestContext>(context);
        var jobRequest = new UnitRequest();
        var jobId = await publisher.Publish(jobRequest);

        await context.SaveChangesAsync();

        var jobFromDb = await GetJobWithStates(context, jobId);

        Assert.NotNull(jobFromDb);
        Assert.Equal(State.Enqueued, jobFromDb.CurrentState);
        Assert.Equal(jobRequest.GetType().AssemblyQualifiedName!, jobFromDb.Type);
        Assert.Equal(JsonSerializer.Serialize(jobRequest), jobFromDb.Message);

        Assert.Single(jobFromDb.JobStates);
        Assert.Equal(State.Enqueued, jobFromDb.JobStates.Single().State);
    }

    [Fact]
    public async Task GetAndProcessJob_ProcessCreatedJob_ShouldBeCompleted()
    {
        var context = CreateContext();

        var testLogId = await CreateLogInDb(context);

        var jobId = await CreateProcessLogJob(context, testLogId);

        await ProcessJob();

        var jobFromDb = await GetJobWithStates(context, jobId);
        var logFromDb = await GetTestLog(context, testLogId);
        
        Assert.Equal(State.Completed, jobFromDb.CurrentState);
        Assert.Equal(2, jobFromDb.JobStates.Count);
        
        Assert.Equal(State.Enqueued, jobFromDb.JobStates.First().State);
        Assert.Equal(State.Completed, jobFromDb.JobStates.Last().State);

        Assert.NotNull(logFromDb.ProcessedTime);
    }

    [Fact]
    public async Task GetAndProcessJob_JobThrowsException_ShouldBeFailed()
    {
        var context = CreateContext();

        var jobId = await CreateFailedJob(context);

        await ProcessJob();

        var jobFromDb = await GetJobWithStates(context, jobId);

        Assert.Equal(State.Failed, jobFromDb.CurrentState);

        Assert.Equal(2, jobFromDb.JobStates.Count);
        Assert.Equal(State.Enqueued, jobFromDb.JobStates.First().State);
        Assert.Equal(State.Failed, jobFromDb.JobStates.Last().State);
    }
}
