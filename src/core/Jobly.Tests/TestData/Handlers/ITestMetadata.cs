using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public partial interface ITestMetadata : IJobMetadata
{
    string TestKey { get; set; }

    int TestCount { get; set; }
}
