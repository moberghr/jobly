using Jobly.Tests.Fixtures;
using Jobly.Tests.Jobs;

namespace Jobly.Tests;

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class SqlServerTests : JoblyTests, IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;

    public SqlServerTests(SqlServerFixture fixture) : base(fixture.CreateContext)
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
