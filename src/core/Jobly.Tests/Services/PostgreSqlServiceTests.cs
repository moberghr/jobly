using Jobly.Tests.Fixtures;

namespace Jobly.Tests.Services;

[Collection("PostgreSql")]
public class PostgreSqlServiceTests : ServiceTests, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlServiceTests(PostgreSqlFixture fixture) : base(fixture.CreateContext)
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
