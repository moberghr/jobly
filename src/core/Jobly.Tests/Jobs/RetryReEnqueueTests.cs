using Jobly.Core.Enums;
using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenFailingJobWithRetries_WhenFailsOnce_ThenReEnqueuedAndCanSucceed()
    {
        var context = CreateContext();
        var jobId = await CreateFailedRetryJob(context, 2, null, null);

        await ProcessJob();

        var jobAfterFirstFailure = await GetJob(jobId);
        jobAfterFirstFailure.CurrentState.ShouldBe(State.Enqueued);
        jobAfterFirstFailure.RetriedTimes.ShouldBe(1);

        await ChangeJobFromException(jobId);

        await ProcessJob();

        var jobAfterSuccess = await GetJob(jobId);
        jobAfterSuccess.CurrentState.ShouldBe(State.Completed);
        jobAfterSuccess.RetriedTimes.ShouldBe(1);
    }
}
