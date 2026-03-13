using System;
using System.Collections.Generic;

namespace WorkIQC.Runtime.Abstractions.Models;

public enum ToolEventType
{
    Started,
    Progress,
    Completed,
    Failed
}

public sealed record ToolEvent
{
    public required string ToolName { get; init; }
    public required ToolEventType EventType { get; init; }
    public string? StatusMessage { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, object?> AdditionalData { get; init; } = new Dictionary<string, object?>();
}
