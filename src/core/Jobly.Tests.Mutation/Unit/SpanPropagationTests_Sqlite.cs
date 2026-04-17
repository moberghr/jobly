using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class SpanPropagationTests_Sqlite : SpanPropagationTestsBase
{
    public SpanPropagationTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
