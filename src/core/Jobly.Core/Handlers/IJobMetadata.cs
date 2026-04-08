namespace Jobly.Core.Handlers;

public interface IJobMetadata
{
    IReadOnlyDictionary<string, string> Metadata { get; }
}
