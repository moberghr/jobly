using Microsoft.EntityFrameworkCore;
using Npgsql;
using Warp.Core.Data;

namespace Warp.Provider.PostgreSql;

internal sealed class PostgresExceptionClassifier : IDatabaseExceptionClassifier
{
    // Classifies a DbUpdateException as a unique/primary-key constraint violation. The circuit
    // breaker first-failure fallback path expects exactly this category (a concurrent worker
    // inserted the row first); any other DbUpdateException — CHECK, FK, column-length — must
    // surface so the operator sees the real defect.
    public bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: "23505" };
    }

    public bool IsTransientDeadlock(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "40P01" or "40001" })
            {
                return true;
            }
        }

        return false;
    }
}
