using Warp.Core.Interfaces;

namespace Warp.Core.Data.Entities;

/// <summary>
/// Persisted state row for a saga instance. One row per live saga (deleted on
/// <c>MarkCompleted</c>). Named <c>SagaState</c> rather than <c>Saga</c> to avoid colliding with
/// the user-facing <c>Warp.Core.Sagas.Saga</c> base class — users writing
/// <c>using Warp.Core.Sagas; using Warp.Core.Data.Entities;</c> would otherwise hit an ambiguity.
/// </summary>
public class SagaState : IConcurrencyToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Type { get; set; } = string.Empty;

    public string CorrelationKey { get; set; } = string.Empty;

    public string StateJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid Version { get; set; }
}
