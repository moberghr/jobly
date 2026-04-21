using Jobly.Test.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public class TestContext : DbContext
{
    public TestContext(DbContextOptions<TestContext> options)
        : base(options)
    {
    }

    public DbSet<Registration> Registrations => Set<Registration>();

    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
}
