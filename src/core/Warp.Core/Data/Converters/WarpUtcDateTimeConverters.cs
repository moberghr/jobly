using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Warp.Core.Data.Converters;

// SQL Server datetime/datetime2 columns carry no timezone marker, so EF Core materializes
// DateTime values with Kind=Unspecified. System.Text.Json then serializes them without a 'Z'
// suffix, and JavaScript Date() parses the string as local time. These converters stamp
// Kind=Utc on read so JSON output stays unambiguous and §5.7's UTC invariant holds end-to-end.
// On Postgres the read-side stamp is needed for `timestamp` (without time zone) columns too —
// it's only a no-op on `timestamptz`, where Npgsql already returns Kind=Utc.
internal static class WarpUtcDateTimeConverters
{
    private static readonly System.Reflection.Assembly WarpCoreAssembly = typeof(WarpUtcDateTimeConverters).Assembly;

    internal static readonly ValueConverter<DateTime, DateTime> UtcDateTime = new(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    internal static readonly ValueConverter<DateTime?, DateTime?> UtcNullableDateTime = new(
        v => v.HasValue ? ToUtcOnWrite(v.Value) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    private static DateTime ToUtcOnWrite(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    // Applied by WarpModelCustomizer in production and by TestContext.OnModelCreating in tests.
    // Scoped to Warp.Core's own entity CLR types (assembly-equality rather than namespace prefix)
    // so the convention can't bleed into a user's entity that happens to live under Warp.*.
    // The convention runs after user OnModelCreating, so a user-supplied converter on a Warp
    // entity property is preserved — we only stamp where nothing is set.
    internal static void ApplyWarpUtcDateTimeConverters(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.Assembly != WarpCoreAssembly)
            {
                continue;
            }

            foreach (var property in entityType.GetProperties())
            {
                if (property.GetValueConverter() is not null)
                {
                    continue;
                }

                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(UtcDateTime);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(UtcNullableDateTime);
                }
            }
        }
    }
}
