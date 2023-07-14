using Microsoft.EntityFrameworkCore;
using Handfire.Core;
using Handfire.Core.Entities;
using Handfire.Core.Enums;

namespace Handfire.BlazorApp.Data;

public class TestContext : DbContext
{
    public TestContext(DbContextOptions<TestContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddOutboxStateEntity();
        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        var job_1 = new Job
        {
            Id = "job 1",
            CreateTime = DateTime.UtcNow,
            Message = "job 1 message",
            Type = "string",
            ScheduleTime = new DateTime(2020, 1, 1),
            CurrentState = State.Completed,
            MaxRetries = 0,
        };

        modelBuilder.Entity<Job>().HasData(job_1);

        var jobState_1 = new JobState
        {
            Id = 1,
            DateTime = DateTime.UtcNow,
            Message = "job state 1 message",
            State = State.Completed,
            JobId = job_1.Id,
        };

        modelBuilder.Entity<JobState>().HasData(jobState_1);
    }
}