using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Warp.Core.Data;

namespace Warp.Provider.SqlServer;

internal sealed class SqlServerExceptionClassifier : IDatabaseExceptionClassifier
{
    // SQL Server error numbers 2627 (unique key violation) and 2601 (duplicate key in unique
    // index) both mean "row already exists". Any other DbUpdateException surfaces.
    public bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException { Number: 2627 or 2601 };
    }

    public bool IsTransientDeadlock(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 })
            {
                return true;
            }
        }

        return false;
    }
}
