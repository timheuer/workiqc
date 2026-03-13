using System;
using System.Collections.Generic;

namespace WorkIQC.Runtime.Abstractions.Models;

public sealed record ChatMessage
{
    public required string Id { get; init; }
    public required MessageRole Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}
