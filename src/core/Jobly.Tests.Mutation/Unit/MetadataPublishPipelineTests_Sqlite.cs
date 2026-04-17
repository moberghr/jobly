using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class MetadataPublishPipelineTests_Sqlite : MetadataPublishPipelineTestsBase
{
    public MetadataPublishPipelineTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
