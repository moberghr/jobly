namespace Warp.Core.Concurrency;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SemaphoreAttribute : Attribute
{
    public SemaphoreAttribute(string key, int limit)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        Key = key;
        Limit = limit;
    }

    public string Key { get; }

    public int Limit { get; }

    public ConcurrencyMode Mode { get; init; } = ConcurrencyMode.Wait;
}
