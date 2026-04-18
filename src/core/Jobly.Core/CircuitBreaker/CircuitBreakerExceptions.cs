using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobly.Core.CircuitBreaker;

internal static class CircuitBreakerExceptions
{
    // Classifies a DbUpdateException as a unique/primary-key constraint violation.
    // The CircuitBreakerStore's first-failure fallback path expects exactly this
    // category (a concurrent worker inserted the row first); any other DbUpdateException
    // — CHECK, FK, column-length — must surface so the operator sees the real defect.
    internal static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException switch
        {
            PostgresException { SqlState: "23505" } => true,
            SqlException { Number: 2627 or 2601 } => true,
            _ => false,
        };
    }
}
