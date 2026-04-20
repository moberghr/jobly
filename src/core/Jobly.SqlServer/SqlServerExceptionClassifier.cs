using Jobly.Core.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Jobly.SqlServer;

internal sealed class SqlServerExceptionClassifier : IDatabaseExceptionClassifier
{
    // SQL Server error numbers 2627 (unique key violation) and 2601 (duplicate key in unique
    // index) both mean "row already exists". Any other DbUpdateException surfaces.
    public bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException { Number: 2627 or 2601 };
    }
}
