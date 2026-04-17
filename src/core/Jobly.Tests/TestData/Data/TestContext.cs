using Jobly.Core;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Tests;

public class TestContext : DbContext
{
    private readonly string? _schema;

    public TestContext(DbContextOptions<TestContext> options, string? schema = "jobly")
        : base(options)
    {
        _schema = schema;
    }

    public DbSet<TestLog> TestLogs => Set<TestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddOutboxStateEntity(_schema);
    }
}
