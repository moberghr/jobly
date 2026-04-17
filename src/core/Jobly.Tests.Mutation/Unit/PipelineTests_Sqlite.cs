using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class PipelineTests_Sqlite : PipelineTestsBase
{
    public PipelineTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
