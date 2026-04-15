namespace Jobly.Core.Handlers;

/// <summary>
/// Marker interface for typed metadata DTOs. Interfaces extending IJobMetadata
/// are discovered by the source generator, which produces an implementation class
/// that extends Dictionary&lt;string, object&gt; with typed property accessors.
/// </summary>
public interface IJobMetadata;
