using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Services;
using Jobly.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobly.Tests.Scheduling;

[GenerateDatabaseTests(FixtureKind.Default)]
public abstract class JobExpirationTimeoutTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected JobExpirationTimeoutTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task DeleteJob_UsesConfiguredExpirationTimeout()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Completed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var config = new JoblyConfiguration { JobExpirationTimeout = TimeSpan.FromHours(2) };
        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(config));

        // Act
        var before = DateTime.UtcNow;
        await svc.DeleteJob(jobId);
        var after = DateTime.UtcNow;

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.ExpireAt.ShouldNotBeNull();
        job.ExpireAt.Value.ShouldBeGreaterThanOrEqualTo(before.AddHours(2).AddSeconds(-1));
        job.ExpireAt.Value.ShouldBeLessThanOrEqualTo(after.AddHours(2).AddSeconds(1));
    }

    [TimedFact]
    public async Task DeleteJob_DefaultTimeout_ExpiresInOneDay()
    {
        // Arrange
        var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Failed,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
        });
        await ctx.SaveChangesAsync();

        var svc = new JobCommandService<TestContext>(_fixture.CreateContext(), TimeProvider.System, Options.Create(new JoblyConfiguration()));

        // Act
        var before = DateTime.UtcNow;
        await svc.DeleteJob(jobId);

        // Assert
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().FindAsync(jobId);
        job.ShouldNotBeNull();
        job.ExpireAt.ShouldNotBeNull();
        job.ExpireAt.Value.ShouldBeGreaterThanOrEqualTo(before.AddDays(1).AddSeconds(-1));
        job.ExpireAt.Value.ShouldBeLessThanOrEqualTo(DateTime.UtcNow.AddDays(1).AddSeconds(1));
    }
}
