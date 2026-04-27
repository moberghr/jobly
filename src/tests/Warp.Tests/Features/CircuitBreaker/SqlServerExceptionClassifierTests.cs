using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Provider.SqlServer;

namespace Warp.Tests.Features.CircuitBreaker;

// Same invariants as PostgresExceptionClassifierTests — only unique/duplicate-index violations
// (SQL Server error 2627 or 2601) may be swallowed by CircuitBreakerStore's first-insert
// fallback; every other error surfaces. Positive-case coverage (a real SqlException with
// Number=2627) is end-to-end only — SqlException's constructors are internal, so we can't
// synthesize one here. The negative paths are the behavior worth pinning at unit-test level.
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
}
