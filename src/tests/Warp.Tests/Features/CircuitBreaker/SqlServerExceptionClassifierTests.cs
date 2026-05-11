using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Warp.Provider.SqlServer;

namespace Warp.Tests.Features.CircuitBreaker;

// Same invariants as PostgresExceptionClassifierTests, but positive cases (a real
// SqlException with Number=2627 / 2601 / 1205) are end-to-end only — SqlException's
// constructors are internal, so we can't synthesize one here. The negative paths plus the
// cross-provider safety check (a PostgresException must NOT match the SQL Server pattern)
// are what we pin at unit-test level.
[Trait("Category", "NoDb")]
public class SqlServerExceptionClassifierTests
{
    private readonly SqlServerExceptionClassifier _sut = new();

    [Fact]
    public void IsUniqueConstraintViolation_GenericInnerException_ReturnsFalse()
    {
        var ex = new DbUpdateException("fail", new InvalidOperationException("boom"));

        _sut.IsUniqueConstraintViolation(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_NoInnerException_ReturnsFalse()
    {
        var ex = new DbUpdateException("fail");

        _sut.IsUniqueConstraintViolation(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsTransientDeadlock_GenericInnerException_ReturnsFalse()
    {
        var ex = new DbUpdateException("fail", new InvalidOperationException("boom"));

        _sut.IsTransientDeadlock(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsTransientDeadlock_NoInnerException_ReturnsFalse()
    {
        var ex = new DbUpdateException("fail");

        _sut.IsTransientDeadlock(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsTransientDeadlock_PostgresException_ReturnsFalse()
    {
        // Cross-provider safety. A Postgres-shaped exception must not match the SQL Server
        // pattern (which is typed against SqlException). If misconfigured DI ever routed a
        // PostgresException to this classifier, the worst case is "no retry, fall through
        // to the existing error path" — never a false-positive retry on the wrong shape.
        var ex = new PostgresException("deadlock detected", "ERROR", "40P01", "40P01");

        _sut.IsTransientDeadlock(ex).ShouldBeFalse();
    }
}
