using Jobly.Core.Data.Entities;
using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Tests.TestData.Handlers;

/// <summary>
/// Adds a Counter entity to the DbContext change tracker then throws WITHOUT calling SaveChanges.
/// Used to verify that handler's unsaved changes don't leak into the worker's save.
/// </summary>
public class AddEntityThenThrowRequest : IJob;

public class AddEntityThenThrowHandler : IJobHandler<AddEntityThenThrowRequest>
{
    private readonly TestContext _context;

    public AddEntityThenThrowHandler(TestContext context)
    {
        _context = context;
    }

    public async Task HandleAsync(AddEntityThenThrowRequest message, CancellationToken cancellationToken)
    {
        _context.Set<Counter>().Add(new Counter { Key = "handler-leaked-entity", Value = 999 });

        throw new InvalidOperationException("Intentional failure after adding entity");
    }
}

/// <summary>
/// Adds a Counter entity, calls SaveChanges, then throws.
/// Used to verify that handler's committed work persists even when the handler fails.
/// </summary>
public class AddEntitySaveThenThrowRequest : IJob;

public class AddEntitySaveThenThrowHandler : IJobHandler<AddEntitySaveThenThrowRequest>
{
    private readonly TestContext _context;

    public AddEntitySaveThenThrowHandler(TestContext context)
    {
        _context = context;
    }

    public async Task HandleAsync(AddEntitySaveThenThrowRequest message, CancellationToken cancellationToken)
    {
        _context.Set<Counter>().Add(new Counter { Key = "handler-committed-entity", Value = 888 });
        await _context.SaveChangesAsync(cancellationToken);

        throw new InvalidOperationException("Intentional failure after saving entity");
    }
}
