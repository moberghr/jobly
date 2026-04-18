using Jobly.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Tests.Mutation.Fixtures;

public class SqliteFixture : IAsyncLifetime, IDatabaseFixture
{
    private SqliteConnection _connection = null!;

    public JoblyTestServer? TestServer => null;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
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
