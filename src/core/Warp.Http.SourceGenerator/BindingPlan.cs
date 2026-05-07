using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Warp.Http.SourceGenerator;

internal enum BindingSource
{
    Body = 1,
    Route = 2,
    Query = 3,
    Header = 4,
}

internal enum BindingShape
{
    /// <summary>Single TRequest parameter — Minimal API binds the whole body for body verbs.</summary>
    WholeBody = 1,

    /// <summary>[AsParameters] TRequest — ASP.NET decomposes per property; no body part.</summary>
    AsParameters = 2,

    /// <summary>Per-source explicit parameters; we construct TRequest in the generated lambda.</summary>
    Mixed = 3,
}

internal sealed class BindingTarget
{
    public BindingTarget(string memberName, ITypeSymbol type, BindingSource source, string sourceKey, int? ctorParameterIndex, string? propertyName)
    {
        MemberName = memberName;
        Type = type;
        Source = source;
        SourceKey = sourceKey;
        CtorParameterIndex = ctorParameterIndex;
        PropertyName = propertyName;
    }

    public string MemberName { get; }

    public ITypeSymbol Type { get; }

    public BindingSource Source { get; }

    public string SourceKey { get; }

    /// <summary>Set when the request type is a record / has a primary ctor.</summary>
    public int? CtorParameterIndex { get; }

    /// <summary>Set when the request type uses a parameterless ctor + property setters.</summary>
    public string? PropertyName { get; }
}

internal sealed class BindingPlan
{
    public BindingPlan(BindingShape shape, IReadOnlyList<BindingTarget> targets, bool usesPrimaryCtor)
    {
        Shape = shape;
        Targets = targets;
        UsesPrimaryCtor = usesPrimaryCtor;
    }

    public BindingShape Shape { get; }

    public IReadOnlyList<BindingTarget> Targets { get; }

    public bool UsesPrimaryCtor { get; }

    public bool HasBodyTargets => Targets.Any(t => t.Source == BindingSource.Body);
}
