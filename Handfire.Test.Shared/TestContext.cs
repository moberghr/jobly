using Handfire.Test.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Core;

public class TestContext : HandfireContext
{
    public TestContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Registration> Registrations => Set<Registration>();

    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
}
