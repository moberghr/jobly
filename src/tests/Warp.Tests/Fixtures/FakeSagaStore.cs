using Microsoft.EntityFrameworkCore;
using Warp.Core.Sagas;

namespace Warp.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="ISagaStore"/> for unit-testing <see cref="SagaHandlerProxy{TSaga, TMessage}"/>.
/// Tracks pending Add/Update/Remove operations and applies them on <see cref="SaveChangesAsync"/>.
/// Setting <see cref="ThrowOnNextSave"/> simulates an optimistic-concurrency violation.
/// </summary>
internal sealed class FakeSagaStore : ISagaStore
{
    private readonly Dictionary<(Type, string), object> _rows = [];
    private readonly List<Action> _pending = [];

    public bool ThrowOnNextSave { get; set; }

    public SagaSaveConflictKind? ThrowConflictKindOnNextSave { get; set; }

    public int LoadCount { get; private set; }

    public int AddCount { get; private set; }

    public int UpdateCount { get; private set; }

    public int RemoveCount { get; private set; }

    public int SaveCount { get; private set; }

    public void Seed<TSaga>(string correlationKey, TSaga saga)
        where TSaga : Saga
    {
        _rows[(typeof(TSaga), correlationKey)] = saga;
    }

    public Task<TSaga?> Load<TSaga>(string correlationKey, CancellationToken cancellationToken)
        where TSaga : Saga, new()
    {
        LoadCount++;
        _rows.TryGetValue((typeof(TSaga), correlationKey), out var saga);

        return Task.FromResult(saga as TSaga);
    }

    public void Add<TSaga>(TSaga saga)
        where TSaga : Saga
    {
        AddCount++;
        _pending.Add(() => _rows[(typeof(TSaga), saga.CorrelationKey)] = saga);
    }

    public void Update<TSaga>(TSaga saga)
        where TSaga : Saga
    {
        UpdateCount++;
        _pending.Add(() => _rows[(typeof(TSaga), saga.CorrelationKey)] = saga);
    }

    public void Remove<TSaga>(TSaga saga)
        where TSaga : Saga
    {
        RemoveCount++;
        _pending.Add(() => _rows.Remove((typeof(TSaga), saga.CorrelationKey)));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        if (ThrowConflictKindOnNextSave is { } kind)
        {
            ThrowConflictKindOnNextSave = null;
            _pending.Clear();
            throw new SagaSaveConflictException(kind, new DbUpdateException($"Simulated {kind} conflict."));
        }

        if (ThrowOnNextSave)
        {
            ThrowOnNextSave = false;

            // Mirror SagaStore: discard pending changes before rethrowing so the proxy and
            // worker outbox see an empty change set. Real store wraps the raw EF Core
            // concurrency exception into a Version-kind conflict; the fake throws the wrapper
            // directly.
            _pending.Clear();
            throw new SagaSaveConflictException(SagaSaveConflictKind.Version, new DbUpdateConcurrencyException("Simulated optimistic-concurrency violation."));
        }

        foreach (var apply in _pending)
        {
            apply();
        }

        _pending.Clear();

        return Task.CompletedTask;
    }

    public void DiscardPendingChanges() => _pending.Clear();

    public int RecordJobLinkCount { get; private set; }

    public int RemoveLinksForSagaCount { get; private set; }

    public void RecordJobLink(Guid sagaId, Guid jobId)
    {
        RecordJobLinkCount++;
    }

    public Task RemoveLinksForSagaAsync(Guid sagaId, CancellationToken cancellationToken)
    {
        RemoveLinksForSagaCount++;
        return Task.CompletedTask;
    }

    public Dictionary<string, int> CounterDeltas { get; } = new(StringComparer.Ordinal);

    public void RecordCounterDelta(string key, int value)
    {
        // Counter writes are staged in the change tracker in production. The fake clears them
        // on ThrowOnNextSave to mirror the real store's tracker-clear-on-conflict behavior.
        _pending.Add(() =>
        {
            CounterDeltas.TryGetValue(key, out var existing);
            CounterDeltas[key] = existing + value;
        });
    }

    public bool ContainsSaga<TSaga>(string correlationKey)
        where TSaga : Saga
        => _rows.ContainsKey((typeof(TSaga), correlationKey));
}
