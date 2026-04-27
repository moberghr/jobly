using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public partial interface ITestMetadata : IJobMetadata
{
    string TestKey { get; set; }

    int TestCount { get; set; }
}
