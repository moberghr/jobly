using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Core.Services;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Admin;

[GenerateDatabaseTests]
public abstract class JobQueryProgressTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected JobQueryProgressTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task GetJobDetailById_ProgressRows_ProjectedWithNameAndValue()
    {
        var jobId = Guid.NewGuid();
        var ctx = _fixture.CreateContext();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        ctx.Set<JobLog>().Add(new JobLog
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            EventType = "Progress",
            Name = "download",
            Value = 88,
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = string.Empty,
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        var svc = new JobQueryService<TestContext>(_fixture.CreateContext(), TimeProvider.System);
        var detail = await svc.GetJobDetailById(jobId);

        detail.ShouldNotBeNull();
        var progressLog = detail.Logs.ShouldHaveSingleItem();
        progressLog.EventType.ShouldBe("Progress");
        progressLog.Name.ShouldBe("download");
        progressLog.Value.ShouldBe((short)88);
    }
}
