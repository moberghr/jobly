using Handfire.Test.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public class TestContext : DbContext
{
    public TestContext(DbContextOptions<TestContext> options)
        : base(options)
    {
    }

    public DbSet<Registration> Registrations => Set<Registration>();

    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddHandfireInterceptors();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddOutboxStateEntity();
    }
}
