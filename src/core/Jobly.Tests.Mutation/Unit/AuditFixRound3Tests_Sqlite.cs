using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class AuditFixRound3Tests_Sqlite : AuditFixRound3TestsBase
{
    public AuditFixRound3Tests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
