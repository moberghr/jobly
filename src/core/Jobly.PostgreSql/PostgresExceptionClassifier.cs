using Jobly.Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobly.PostgreSql;

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
}
