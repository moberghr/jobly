using Jobly.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jobly.Core.Interceptors;
public class SaveChangesConcurrencyTokenInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var concurrencyTokenEntities = eventData
            .Context?
            .ChangeTracker
            .Entries<IConcurrencyToken>()
            .Where(x =>
                x.State == EntityState.Modified
                || x.State == EntityState.Added);

        if(concurrencyTokenEntities is not null)
        {
            foreach (var entity in concurrencyTokenEntities)
            {
                entity.Entity.Version = Guid.NewGuid();
            }
        }

        return new(result);
    }
}
