namespace Warp.Tests.Fixtures;

public interface IDatabaseFixture
{
    TestContext CreateContext();

    Task ResetAsync();
}
