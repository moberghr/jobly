using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class AuditFixRound2Tests_Sqlite : AuditFixRound2TestsBase
{
    public AuditFixRound2Tests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
