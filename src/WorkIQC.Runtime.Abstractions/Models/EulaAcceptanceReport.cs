namespace WorkIQC.Runtime.Abstractions.Models;

public enum EulaAcceptanceStatus
{
    Accepted,
    ActionRequired,
    Unknown
}

public sealed record EulaAcceptanceReport
{
    public required EulaAcceptanceStatus Status { get; init; }
    public required string MarkerPath { get; init; }
    public string? Details { get; init; }
    public string? Resolution { get; init; }
    public bool CanProceed => Status == EulaAcceptanceStatus.Accepted;
}
