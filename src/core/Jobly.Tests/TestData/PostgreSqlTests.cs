using Jobly.Tests.Fixtures;
using Jobly.Tests.Jobs;

namespace Jobly.Tests;

[Collection("PostgreSql")]
public class PostgreSqlTests : JoblyTests, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlTests(PostgreSqlFixture fixture) : base(fixture.CreateContext)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        ResetServerRegistration();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
