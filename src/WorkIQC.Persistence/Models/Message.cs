namespace WorkIQC.Persistence.Models;

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; } // JSON blob for tool calls, etc.

    public Conversation? Conversation { get; set; }
}
