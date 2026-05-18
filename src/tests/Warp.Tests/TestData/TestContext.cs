using Microsoft.EntityFrameworkCore;
using Warp.Core;
using Warp.Core.Data.Converters;

namespace Warp.Tests;

public class TestContext : DbContext
{
    private readonly string? _schema;

    public TestContext(DbContextOptions<TestContext> options, string? schema = "warp")
        : base(options)
    {
        _schema = schema;
    }

    public DbSet<TestLog> TestLogs => Set<TestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddOutboxStateEntity(_schema);

        // Tests use the CircuitBreaker addon, which contributes its own entity via
        // WarpConfiguration.EntityConfigurators when AddWarpCircuitBreaker is called.
        // TestContext is constructed directly by fixtures without going through DI,
        // so we must explicitly include the addon entity here.
        ServiceConfiguration.AddCircuitBreakerStateEntity(modelBuilder, _schema);
        ServiceConfiguration.AddConcurrencyLimitEntity(modelBuilder, _schema);
        ServiceConfiguration.AddRateLimitBucketEntity(modelBuilder, _schema);
        ServiceConfiguration.AddRateLimitOverrideEntity(modelBuilder, _schema);
        ServiceConfiguration.AddSagaStateEntity(modelBuilder, _schema);
        ServiceConfiguration.AddSagaJobLinkEntity(modelBuilder, _schema);
        ServiceConfiguration.AddBackgroundServiceDefinitionEntity(modelBuilder, _schema);
        ServiceConfiguration.AddBackgroundServiceInstanceEntity(modelBuilder, _schema);
        ServiceConfiguration.AddBackgroundServiceLeaseEntity(modelBuilder, _schema);
        ServiceConfiguration.AddBackgroundServiceLogEntity(modelBuilder, _schema);

        // Mirror what WarpModelCustomizer does in production. The unit-fixture path bypasses
        // ReplaceService<IModelCustomizer> by building DbContextOptions directly, so we apply
        // the convention here to keep fixture-backed tests aligned with real DI behavior.
        modelBuilder.ApplyWarpUtcDateTimeConverters();
    }
}
