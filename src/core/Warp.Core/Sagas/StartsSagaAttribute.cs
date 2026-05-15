namespace Warp.Core.Sagas;

/// <summary>
/// Marks an <c>IMessage</c> type as one that creates a new saga when no matching correlation
/// key exists. A message without this attribute arriving for an unknown correlation key triggers
/// <c>ISagaHandler.NotFoundAsync</c> instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StartsSagaAttribute : Attribute;
