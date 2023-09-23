using Jobly.Core;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Tests;

public class TestContext : DbContext
{
    public TestContext(DbContextOptions<TestContext> options)
        : base(options)
    {
    }

    public DbSet<TestLog> TestLogs => Set<TestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddOutboxStateEntity();
    }
}
