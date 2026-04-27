using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Warp.Tests.Fixtures;

namespace Warp.Tests.Mutation.Fixtures;

public class SqliteFixture : IAsyncLifetime, IDatabaseFixture
{
    private SqliteConnection _connection = null!;

    public WarpTestServer? TestServer => null;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync(Xunit.TestContext.Current.CancellationToken);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync(Xunit.TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(Xunit.TestContext.Current.CancellationToken);
    }

    public TestContext CreateContext()
    {
        return new TestContext(
            new DbContextOptionsBuilder<TestContext>()
                .UseSqlite(_connection)
                .Options,
            schema: null);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

[CollectionDefinition]
public class SqliteCollection : ICollectionFixture<SqliteFixture>;
