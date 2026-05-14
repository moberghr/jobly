using Microsoft.EntityFrameworkCore;
using Warp.Core.Data;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Shared no-op <see cref="IDatabaseExceptionClassifier"/> for tests that don't depend on
/// the classifier branching. Configure flags per test if a specific path needs to fire.
/// </summary>
internal sealed class FakeExceptionClassifier : IDatabaseExceptionClassifier
{
    public bool TreatAllUpdateExceptionsAsUniqueViolation { get; set; }

    public bool TreatAllAsTransientDeadlock { get; set; }

    public bool IsUniqueConstraintViolation(DbUpdateException ex) => TreatAllUpdateExceptionsAsUniqueViolation;

    public bool IsTransientDeadlock(Exception ex) => TreatAllAsTransientDeadlock;
}
