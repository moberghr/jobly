using Jobly.Core.CircuitBreaker;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace Jobly.Tests.Unit;

// SQL Server coverage for IsUniqueConstraintViolation happens end-to-end via the
// RecordFailure_ConcurrentFirstFailures_AllCounted_SqlServer variant of CircuitBreakerTests:
// it forces a real PK collision against a live SQL Server, and the fallback ExecuteUpdate
// only runs if the when-clause accepts the generated SqlException. A synthetic SqlException
// unit test is impractical because the type's constructors are internal; end-to-end coverage
// on both providers is sufficient.
[Trait("Category", "NoDb")]
public class CircuitBreakerExceptionsTests
{
    [Fact]
    public void IsUniqueConstraintViolation_NpgsqlUniqueViolation_ReturnsTrue()
    {
        var inner = new PostgresException("duplicate key", "ERROR", "ERROR", "23505");
        var ex = new DbUpdateException("save failed", inner);

        CircuitBreakerExceptions.IsUniqueConstraintViolation(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsUniqueConstraintViolation_NpgsqlCheckViolation_ReturnsFalse()
    {
        // Regression for PR #126 review F1: the catch in CircuitBreakerStore.RecordFailureAsync
        // was catching every DbUpdateException, so a CHECK constraint violation (23514) could be
        // silently swallowed if a concurrent worker happened to insert the row in between.
        var inner = new PostgresException("check constraint violated", "ERROR", "ERROR", "23514");
        var ex = new DbUpdateException("save failed", inner);

        CircuitBreakerExceptions.IsUniqueConstraintViolation(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_NpgsqlForeignKeyViolation_ReturnsFalse()
    {
        var inner = new PostgresException("fk violated", "ERROR", "ERROR", "23503");
        var ex = new DbUpdateException("save failed", inner);

        CircuitBreakerExceptions.IsUniqueConstraintViolation(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_GenericInnerException_ReturnsFalse()
    {
        var ex = new DbUpdateException("save failed", new InvalidOperationException("not a db error"));

        CircuitBreakerExceptions.IsUniqueConstraintViolation(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_NoInnerException_ReturnsFalse()
    {
        var ex = new DbUpdateException("save failed");

        CircuitBreakerExceptions.IsUniqueConstraintViolation(ex).ShouldBeFalse();
    }
}
