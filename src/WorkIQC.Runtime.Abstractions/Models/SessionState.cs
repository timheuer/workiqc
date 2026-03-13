using System;

namespace WorkIQC.Runtime.Abstractions.Models;

public enum SessionStatus
{
    Initializing,
    Ready,
    Processing,
    Failed,
    Disposed
}

public sealed record SessionState
{
    public required string SessionId { get; init; }
    public required SessionStatus Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastActivityAt { get; init; }
}
