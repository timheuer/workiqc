namespace WorkIQC.Runtime.Abstractions.Models;

public sealed record SendMessageResponse
{
    public required string SessionId { get; init; }
    public required string MessageId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Success => string.IsNullOrWhiteSpace(ErrorCode) && ErrorMessage is null;
}
