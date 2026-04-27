namespace Warp.Core.Mutex;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MutexAttribute : Attribute
{
    public MutexAttribute(string key)
    {
        Key = key;
    }

    public string Key { get; }
}
