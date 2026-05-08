namespace Warp.Core.Concurrency;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MutexAttribute : Attribute
{
    public MutexAttribute(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        Key = key;
    }

    public string Key { get; }

    public ConcurrencyMode Mode { get; init; } = ConcurrencyMode.Skip;
}
