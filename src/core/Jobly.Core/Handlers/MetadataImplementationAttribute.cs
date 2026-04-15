namespace Jobly.Core.Handlers;

[AttributeUsage(AttributeTargets.Interface)]
public sealed class MetadataImplementationAttribute : Attribute
{
    public Type ImplementationType { get; }

    public MetadataImplementationAttribute(Type implementationType)
    {
        ImplementationType = implementationType;
    }
}
