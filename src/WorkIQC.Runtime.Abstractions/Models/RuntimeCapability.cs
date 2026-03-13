namespace WorkIQC.Runtime.Abstractions.Models;

public enum RuntimeCapabilityStatus
{
    Available,
    ActionRequired,
    Unavailable
}

public sealed record RuntimeCapability
{
    public required string Name { get; init; }
    public required RuntimeCapabilityStatus Status { get; init; }
    public string? Details { get; init; }
    public string? Resolution { get; init; }
}
