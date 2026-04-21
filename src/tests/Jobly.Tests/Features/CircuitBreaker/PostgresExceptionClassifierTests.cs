using Jobly.Provider.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace Jobly.Tests.Features.CircuitBreaker;

// Pinned behavior from the old CircuitBreakerExceptionsTests (PR #126 regression fix):
// only unique/primary-key violations (SQLSTATE 23505) may be swallowed by the first-insert
// fallback in CircuitBreakerStore.RecordFailureAsync. Every other DbUpdateException — CHECK,
// FK, column-length, etc. — must surface so the operator sees the real defect. These tests
// live in Jobly.Tests because PostgresExceptionClassifier is internal; InternalsVisibleTo is
// granted to Jobly.Tests on the provider package.
[Trait("Category", "NoDb")]
public class PostgresExceptionClassifierTests
{
    private readonly PostgresExceptionClassifier _sut = new();

    [Fact]
    public void IsUniqueConstraintViolation_NpgsqlUniqueViolation_ReturnsTrue()
    {
        var ex = new DbUpdateException("fail", new PostgresException("dup", "ERROR", "23505", "23505"));

        _sut.IsUniqueConstraintViolation(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsUniqueConstraintViolation_NpgsqlCheckViolation_ReturnsFalse()
    {
        // CHECK violation is SQLSTATE 23514 — must NOT be swallowed, or a data bug hides forever.
        var ex = new DbUpdateException("fail", new PostgresException("check", "ERROR", "23514", "23514"));

        _sut.IsUniqueConstraintViolation(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_NpgsqlForeignKeyViolation_ReturnsFalse()
    {
        // FK violation (23503) — must NOT be swallowed.
        var ex = new DbUpdateException("fail", new PostgresException("fk", "ERROR", "23503", "23503"));

        _sut.IsUniqueConstraintViolation(ex).ShouldBeFalse();
    }

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
