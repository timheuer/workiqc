namespace WorkIQC.Runtime.Abstractions.Models;

public sealed record StreamingDelta
{
    public required string Content { get; init; }
    public bool IsComplete { get; init; }
}
