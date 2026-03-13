using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkIQC.Runtime.Abstractions.Models;

public sealed record RuntimeReadinessReport
{
    public required string Subject { get; init; }
    public string? RequestedVersion { get; init; }
    public IReadOnlyList<DependencyCheckResult> Dependencies { get; init; } = Array.Empty<DependencyCheckResult>();
    public IReadOnlyList<RuntimeCapability> Capabilities { get; init; } = Array.Empty<RuntimeCapability>();

    public bool IsReady =>
        Dependencies.All(dependency => dependency.IsAvailable) &&
        Capabilities.All(capability => capability.Status == RuntimeCapabilityStatus.Available);
}
