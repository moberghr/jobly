using Shouldly;

namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    [Fact]
    public async Task GivenEmptyQueue_WhenProcessed_ThenReturnsFalse()
    {
        var result = await TryProcessJob();

        result.ShouldBeFalse();
    }
}
