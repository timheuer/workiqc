namespace WorkIQC.Runtime.Abstractions.Models;

public sealed record DependencyCheckResult
{
    public required string Name { get; init; }
    public bool IsAvailable { get; init; }
    public string? ResolvedPath { get; init; }
    public string? Details { get; init; }
}
