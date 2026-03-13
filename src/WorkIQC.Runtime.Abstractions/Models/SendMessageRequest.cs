using System;

namespace WorkIQC.Runtime.Abstractions.Models;

public sealed record SendMessageRequest
{
    public required string ConversationId { get; init; }
    public required string UserMessage { get; init; }
    public string? SessionId { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConversationId))
        {
            throw new ArgumentException("ConversationId is required.", nameof(ConversationId));
        }

        if (string.IsNullOrWhiteSpace(UserMessage))
        {
            throw new ArgumentException("UserMessage is required.", nameof(UserMessage));
        }
    }
}
