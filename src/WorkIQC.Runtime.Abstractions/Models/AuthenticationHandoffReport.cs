namespace WorkIQC.Runtime.Abstractions.Models;

public enum AuthenticationHandoffStatus
{
    Completed,
    ActionRequired,
    Unknown
}

public sealed record AuthenticationHandoffReport
{
    public required AuthenticationHandoffStatus Status { get; init; }
    public required string MarkerPath { get; init; }
    public required string LoginCommand { get; init; }
    public string? Details { get; init; }
    public string? Resolution { get; init; }
    public bool CanProceed => Status == AuthenticationHandoffStatus.Completed;
}
