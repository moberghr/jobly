using Jobly.Tests.Fixtures;

namespace Jobly.Tests.Services;

[Collection("SqlServer")]
[Trait("Category", "SqlServer")]
public class SqlServerServiceTests : ServiceTests, IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;

    public SqlServerServiceTests(SqlServerFixture fixture) : base(fixture.CreateContext)
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
