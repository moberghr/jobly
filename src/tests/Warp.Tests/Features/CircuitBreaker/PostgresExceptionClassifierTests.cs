using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Warp.Provider.PostgreSql;

namespace Warp.Tests.Features.CircuitBreaker;

// Pinned behavior from the old CircuitBreakerExceptionsTests (PR #126 regression fix):
// only unique/primary-key violations (SQLSTATE 23505) may be swallowed by the first-insert
// fallback in CircuitBreakerStore.RecordFailureAsync. Every other DbUpdateException — CHECK,
// FK, column-length, etc. — must surface so the operator sees the real defect. The
// IsTransientDeadlock tests additionally pin which SQLSTATEs drive the CompletionBatch
// retry loop. These tests live in Warp.Tests because PostgresExceptionClassifier is
// internal; InternalsVisibleTo is granted to Warp.Tests on the provider package.
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

    [Fact]
    public void IsTransientDeadlock_DeadlockDetected_ReturnsTrue()
    {
        // SQLSTATE 40P01 — deadlock monitor picked us as victim. Retryable.
        var ex = new PostgresException("deadlock detected", "ERROR", "40P01", "40P01");

        _sut.IsTransientDeadlock(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransientDeadlock_SerializationFailure_ReturnsTrue()
    {
        // SQLSTATE 40001 — serializable-snapshot read/write dependency conflict. Retryable.
        var ex = new PostgresException("could not serialize access", "ERROR", "40001", "40001");

        _sut.IsTransientDeadlock(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransientDeadlock_WrappedInDbUpdateException_ReturnsTrue()
    {
        // EF Core wraps Npgsql exceptions in DbUpdateException on SaveChanges paths; the
        // classifier walks the InnerException chain so the wrapper doesn't hide the SQLSTATE.
        var inner = new PostgresException("deadlock detected", "ERROR", "40P01", "40P01");
        var ex = new DbUpdateException("save failed", inner);

        _sut.IsTransientDeadlock(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsTransientDeadlock_UniqueViolation_ReturnsFalse()
    {
        // 23505 is a unique-constraint violation, not a transient conflict — retrying would
        // just hit the same duplicate row. Must NOT report transient.
        var ex = new PostgresException("dup", "ERROR", "23505", "23505");

        _sut.IsTransientDeadlock(ex).ShouldBeFalse();
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
}
