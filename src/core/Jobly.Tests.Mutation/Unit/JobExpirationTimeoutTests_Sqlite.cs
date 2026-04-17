using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class JobExpirationTimeoutTests_Sqlite : JobExpirationTimeoutTestsBase
{
    public JobExpirationTimeoutTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
