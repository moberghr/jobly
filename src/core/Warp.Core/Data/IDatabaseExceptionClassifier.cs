using Microsoft.EntityFrameworkCore;

namespace Warp.Core.Data;

/// <summary>
/// Classifies provider-specific database exceptions into Core-recognisable categories.
/// Registered by provider packages (Warp.PostgreSql, Warp.SqlServer) via their
/// <c>UsePostgreSql</c>/<c>UseSqlServer</c> builder extensions. Core uses the classifier
/// to keep provider types out of its own dependency graph.
/// </summary>
public interface IDatabaseExceptionClassifier
{
    /// <summary>
    /// True if the given <see cref="DbUpdateException"/> is a unique / primary-key constraint
    /// violation (PG SQLSTATE 23505, SQL Server error 2627/2601).
    /// </summary>
    bool IsUniqueConstraintViolation(DbUpdateException ex);
}
