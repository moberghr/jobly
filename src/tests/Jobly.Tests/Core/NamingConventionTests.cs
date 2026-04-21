using Jobly.Core;
using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Core;

[Trait("Category", "NoDb")]
public class NamingConventionTests
{
    [TimedFact]
    public void JoblyEntities_UsePascalCaseTableNames_WhenNoConventionApplied()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseSqlServer("Server=dummy")
            .Options;

        using var context = new TestContext(options);
        var model = context.Model;

        model.FindEntityType(typeof(Job))!.GetTableName().ShouldBe("Job");
        model.FindEntityType(typeof(RecurringJob))!.GetTableName().ShouldBe("RecurringJob");
        model.FindEntityType(typeof(JobLog))!.GetTableName().ShouldBe("JobLog");
        model.FindEntityType(typeof(Server))!.GetTableName().ShouldBe("Server");
        model.FindEntityType(typeof(Jobly.Core.Data.Entities.Worker))!.GetTableName().ShouldBe("Worker");
        model.FindEntityType(typeof(WorkerGroup))!.GetTableName().ShouldBe("WorkerGroup");
        model.FindEntityType(typeof(Counter))!.GetTableName().ShouldBe("Counter");
        model.FindEntityType(typeof(Statistic))!.GetTableName().ShouldBe("Statistic");
        model.FindEntityType(typeof(ServerTask))!.GetTableName().ShouldBe("ServerTask");
        model.FindEntityType(typeof(ServerLog))!.GetTableName().ShouldBe("ServerLog");
    }

    [TimedFact]
    public void JoblyEntities_UseSnakeCaseTableNames_WhenSnakeCaseConventionApplied()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=dummy")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new TestContext(options);
        var model = context.Model;

        model.FindEntityType(typeof(Job))!.GetTableName().ShouldBe("job");
        model.FindEntityType(typeof(RecurringJob))!.GetTableName().ShouldBe("recurring_job");
        model.FindEntityType(typeof(RecurringJobLog))!.GetTableName().ShouldBe("recurring_job_log");
        model.FindEntityType(typeof(JobLog))!.GetTableName().ShouldBe("job_log");
        model.FindEntityType(typeof(Server))!.GetTableName().ShouldBe("server");
        model.FindEntityType(typeof(Jobly.Core.Data.Entities.Worker))!.GetTableName().ShouldBe("worker");
        model.FindEntityType(typeof(WorkerGroup))!.GetTableName().ShouldBe("worker_group");
        model.FindEntityType(typeof(Counter))!.GetTableName().ShouldBe("counter");
        model.FindEntityType(typeof(Statistic))!.GetTableName().ShouldBe("statistic");
        model.FindEntityType(typeof(ServerTask))!.GetTableName().ShouldBe("server_task");
        model.FindEntityType(typeof(ServerLog))!.GetTableName().ShouldBe("server_log");
    }

    [TimedFact]
    public void JoblyEntities_UseSnakeCaseColumnNames_WhenSnakeCaseConventionApplied()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=dummy")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new TestContext(options);
        var jobEntity = context.Model.FindEntityType(typeof(Job))!;

        jobEntity.FindProperty(nameof(Job.CurrentState))!.GetColumnName().ShouldBe("current_state");
        jobEntity.FindProperty(nameof(Job.CreateTime))!.GetColumnName().ShouldBe("create_time");
        jobEntity.FindProperty(nameof(Job.ScheduleTime))!.GetColumnName().ShouldBe("schedule_time");
        jobEntity.FindProperty(nameof(Job.ParentJobId))!.GetColumnName().ShouldBe("parent_job_id");
        jobEntity.FindProperty(nameof(Job.HandlerType))!.GetColumnName().ShouldBe("handler_type");
        jobEntity.FindProperty(nameof(Job.CancellationMode))!.GetColumnName().ShouldBe("cancellation_mode");
    }

    [TimedFact]
    public void JoblyEntities_UseDefaultJoblySchema()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseSqlServer("Server=dummy")
            .Options;

        using var context = new TestContext(options);
        var model = context.Model;

        model.FindEntityType(typeof(Job))!.GetSchema().ShouldBe("jobly");
        model.FindEntityType(typeof(RecurringJob))!.GetSchema().ShouldBe("jobly");
        model.FindEntityType(typeof(JobLog))!.GetSchema().ShouldBe("jobly");
        model.FindEntityType(typeof(Server))!.GetSchema().ShouldBe("jobly");
        model.FindEntityType(typeof(Jobly.Core.Data.Entities.Worker))!.GetSchema().ShouldBe("jobly");
        model.FindEntityType(typeof(Counter))!.GetSchema().ShouldBe("jobly");
    }

    [TimedFact]
    public void JoblyEntities_UseCustomSchema_WhenOverridden()
    {
        var options = new DbContextOptionsBuilder<SchemaTestContext>()
            .UseSqlServer("Server=dummy")
            .Options;

        using var context = new SchemaTestContext(options);
        var model = context.Model;

        model.FindEntityType(typeof(Job))!.GetSchema().ShouldBe("custom");
    }

    [TimedFact]
    public void JoblyEntities_UseNullSchema_WhenSetToNull()
    {
        var options = new DbContextOptionsBuilder<NullSchemaTestContext>()
            .UseSqlServer("Server=dummy")
            .Options;

        using var context = new NullSchemaTestContext(options);
        var model = context.Model;

        model.FindEntityType(typeof(Job))!.GetSchema().ShouldBeNull();
    }
}

internal class SchemaTestContext : DbContext
{
    public SchemaTestContext(DbContextOptions<SchemaTestContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddOutboxStateEntity("custom");
    }
}

internal class NullSchemaTestContext : DbContext
{
    public NullSchemaTestContext(DbContextOptions<NullSchemaTestContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddOutboxStateEntity(null);
    }
}
